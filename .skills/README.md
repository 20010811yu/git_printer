# Cline Skills 技能库

本目录存放项目级技能（Skills）：可复用的工作流、工具用法与最佳实践。
Cline 在执行任务时应优先查阅并遵循本目录下的技能规范。

## 📚 技能索引

| 技能 | 目录 | 用途 | 状态 |
|------|------|------|------|
| 可靠搜索/替换工具包 | [`replace-search/SKILL.md`](replace-search/SKILL.md) | 中文/CRLF/C# 项目中可靠的代码定位与编辑工作流（findstr / Select-String / 脚本），解决内置搜索空返回、替换失败问题 | ✅ 可用 |
| WinForms MVVM 开发规范 | [`winforms-mvvm/SKILL.md`](winforms-mvvm/SKILL.md) | 本项目 MVVM 四层架构约定、WinForms 绑定技巧、自绘控件规范、踩坑记录 | ✅ 可用 |

## 📁 目录约定

```
.skills/
├── README.md                # 本索引文件
└── <skill-name>/
    ├── SKILL.md             # 技能主文档（何时用 / 怎么用 / 铁律）
    └── scripts/             # （可选）技能配套脚本
```

## ➕ 新增技能

1. 在 `.skills/` 下建 `<skill-name>/` 目录（kebab-case 命名）
2. 编写 `SKILL.md`：说明**适用场景 → 操作步骤 → 铁律/注意事项**
3. 在本 README 索引表中登记

## 🔗 相关文档

- `.clinerules/code-rules.md` —— 强制编码规则（MVVM 架构约束）
- `.clinerules/memory-bank.md` —— 项目记忆机制说明
- `memory-bank/` —— 项目上下文记忆（每次任务开始必读）