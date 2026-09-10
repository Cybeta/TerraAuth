// Phase 6 - 配置服务接口
// 对应架构文档 §4.5 基础设施层

namespace TerraAuth.Config;

/// <summary>
/// 配置服务：负责加载、校验、热重载服务端配置。
/// 所有阈值（速度上限、伤害上限、速率）从此处读取，
/// 修改配置无需重新编译。
/// </summary>
public interface IConfigurationService
{
    /// <summary>当前生效的服务端配置（快照，线程安全）。</summary>
    ServerConfig Current { get; }

    /// <summary>配置变更事件（热重载时触发，观察者模式）。</summary>
    event Action<ServerConfig> OnChanged;

    /// <summary>按路径加载配置文件并校验，校验失败抛异常。</summary>
    void Load(string path);

    /// <summary>重新加载（热更新），成功则触发 OnChanged。</summary>
    void Reload();
}
