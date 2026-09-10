// TerraAuth — Phase 5: TCP 分帧（解决半包 / 粘包）
// Terraria 协议使用 [长度前缀 + 包类型 + payload] 的帧格式
// 参考 terraria-protocol 的 framing 规范

using System.Buffers;
using System.IO;            // InvalidDataException
using System.IO.Pipelines; // ReadOnlySequence<byte>
using TerraAuth.Protocol;   // PacketId

namespace TerraAuth.Net.Phase5;

/// <summary>
/// 帧格式（与 Terraria 原版一致）：
///   [UInt16  length]  — 整包字节数，含 length 自身与 type，即 length = 3 + payload.Length
///   [Byte     type]   — PacketId
///   [Byte[]  payload] — 包体
/// </summary>
public static class Framing
{
    public const int HeaderLength = 3; // UInt16 length + Byte type
    public const int MaxFrameLength = ushort.MaxValue; // 整包上限（length 字段为 UInt16）

    /// <summary>
    /// 将一条完整帧写入输出。供 PacketEncoder 调用。
    /// </summary>
    public static void WriteFrame(IBufferWriter<byte> writer, PacketId type, ReadOnlySpan<byte> payload)
    {
        int total = HeaderLength + payload.Length;
        if (total > MaxFrameLength)
            throw new InvalidOperationException($"Frame too large: {total} bytes");

        var span = writer.GetSpan(total);
        // 小端：length = 整包字节数（含 2 字节长度头 + 1 字节类型）
        span[0] = (byte)(total & 0xFF);
        span[1] = (byte)((total >> 8) & 0xFF);
        span[2] = (byte)type;
        payload.CopyTo(span[HeaderLength..]);
        writer.Advance(total);
    }

    /// <summary>
    /// 尝试从 PipeReader 读取一帧。
    /// 返回 true 表示拿到完整帧；false 表示数据不足（调用方应继续读）。
    /// </summary>
    public static bool TryReadFrame(
        ref ReadOnlySequence<byte> buffer,
        out PacketId type,
        out ReadOnlySequence<byte> payload)
    {
        type = default;
        payload = default;

        // 至少需要 3 字节头
        if (buffer.Length < HeaderLength)
            return false;

        Span<byte> header = stackalloc byte[HeaderLength];
        buffer.Slice(0, HeaderLength).CopyTo(header);

        ushort length = (ushort)(header[0] | (header[1] << 8));
        type = (PacketId)header[2];

        // length 是整包字节数，至少需容纳 3 字节头
        if (length < HeaderLength)
            throw new InvalidDataException($"Invalid frame length {length} (< header)");

        long totalFrame = length;
        if (buffer.Length < totalFrame)
            return false; // 半包：等更多数据

        payload = buffer.Slice(HeaderLength, length - HeaderLength);
        buffer = buffer.Slice(totalFrame); // 前进（粘包时保留剩余）
        return true;
    }
}
