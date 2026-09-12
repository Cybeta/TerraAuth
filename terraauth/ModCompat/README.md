# ModCompat 未来兼容层

## 当前状态

`ModCompat` 是未来单独立项的 MOD 兼容层，不属于当前生产网络能力。

当前生产版本明确为 Vanilla-only：

- 仅支持原版 Terraria 客户端；
- 支持 Terraria 协议 326；
- 不接受 TModLoader 握手；
- 不注册 250-255 自定义包；
- 不转发 MOD、自定义或未知包；
- 未知包由权威管线拒绝。

相关代码仅用于保留未来扩展边界，默认 `enabled: false`，不得据此宣称当前支持 MOD。

## 未来范围

未来独立立项时，兼容层可以单独评估以下能力：

- 客户端类型与版本检测；
- Mod 列表解析和策略校验；
- TModLoader 握手流程；
- 自定义包注册、编解码和权限控制；
- 与 Vanilla 权威状态链路隔离的兼容适配器。

这些能力必须在独立兼容层中重新完成协议、权限、限流、审计和测试设计，不能绕过当前的：

```text
客户端包
→ Authority 只读校验
→ Command 入队
→ WorldSimulator.Tick()
→ Command.Apply()
→ WorldState 权威提交
→ 服务端最终状态同步
```

## 代码状态

`TModLoaderCompat` 保留为未来实现，但当前组合根显式使用 `enabled: false`。禁用时：

- 不注册 MOD 包范围；
- 拒绝 MOD 握手并返回 `mod_support_disabled`；
- 忽略或阻止 MOD 自定义包处理；
- 不提供任意原始包发送入口。

未来重新启用前，必须先单独完成兼容层设计、协议测试和生产开关审查。
