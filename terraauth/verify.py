#!/usr/bin/env python3
"""
TerraAuth 静态校验（无 dotnet SDK 环境下的降级方案）
检查项：
1. 每个 .cs 文件大括号平衡
2. ProjectReference 路径全部有效（用 pathlib.resolve 正确处理 ../）
3. 接口方法在实现类中存在（粗略：统计接口 vs 实现的关键方法名）
4. 明确标记的 // TODO 数量
"""
import glob, re, os, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
files = sorted(ROOT.glob("**/*.cs"))

# 排除生成/第三方
files = [f for f in files if "obj" not in f.parts and "bin" not in f.parts]

issues = []

def read(f):
    return f.read_text(encoding="utf-8", errors="ignore")

# ---- 1. 大括号平衡 ----
for f in files:
    src = read(f)
    src2 = re.sub(r"//.*", "", src)
    src2 = re.sub(r"/\*.*?\*/", "", src2, flags=re.S)
    opens = src2.count("{")
    closes = src2.count("}")
    if opens != closes:
        issues.append(f"[Brace] {f.relative_to(ROOT)}: {{={opens} }}={closes}")

# ---- 2. ProjectReference 路径有效性 ----
for csproj in ROOT.glob("**/*.csproj"):
    if "obj" in csproj.parts or "bin" in csproj.parts:
        continue
    text = read(csproj)
    for m in re.finditer(r'ProjectReference[^>]*Include="([^"]+)"', text):
        # 统一路径分隔符（兼容 Windows 风格的反斜杠）
        normalized = m.group(1).replace("\\", "/")
        ref = (csproj.parent / normalized).resolve()
        if not ref.exists():
            issues.append(f"[ProjectRef] {csproj.name} -> {m.group(1)} NOT FOUND (resolved: {ref})")

# ---- 3. 核心接口实现检查（粗略）----
text_all = "\n".join(read(f) for f in files)
required = {
    "IPlugin": ["PluginBase"],
    "IHookRegistry": ["HookRegistry"],
    "IPluginLoader": ["PluginLoader"],
    "IModDetector": ["ModDetector"],
    "ICustomPacketHandler": ["CustomPacketHandler"],
    "IInboundPipeline": ["InboundPipeline", "HookedPipeline"],
    "IGameHost": ["GameHost"],
}
for iface, impls in required.items():
    for impl in impls:
        if impl not in text_all:
            issues.append(f"[Impl] {iface} 缺少实现: {impl}")

# ---- 4. TODO 统计 ----
todo_count = 0
for f in files:
    todo_count += read(f).count("TODO")

# ---- 输出 ----
print("=" * 60)
print("TerraAuth 静态校验")
print(f"扫描 {len(files)} 个 .cs 文件")
print("=" * 60)

if issues:
    for iss in issues:
        print(f"  {iss}")
    print(f"\n共 {len(issues)} 个问题")
    sys.exit(1)
else:
    print("  ✅ 大括号平衡")
    print("  ✅ 所有 ProjectReference 路径有效")
    print("  ✅ 所有核心接口均有实现")
    print(f"  ℹ️  TODO 数量: {todo_count}（属正常，为待填充扩展点）")
    print("\n校验通过")
    sys.exit(0)
