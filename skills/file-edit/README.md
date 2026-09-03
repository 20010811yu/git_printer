# file-edit —— 可靠的文件编辑工具（2026-08-20）

## 为什么需要它

write_to_file / replace_in_file 编辑工具在多会话中频繁假成功（返回成功但磁盘无变化/文件名截断/内容残缺），
且 cmd→PowerShell 单行命令传中文/尖括号/特殊符号会被传输层损坏（问题汇总 #12/#13）。
本脚本经**参数传值 + 内容文件中转**，从根源规避两类问题，并**内置写后自动回读验证**——成功回执可信任。

## 用法（七操作）

查：
- show -File x.cs -Around 100 —— 查看第 100 行前后各 5 行（定位用，先看再改）
- view -File x.cs -From 95 -To 105 —— 查看指定行区间（含行号）

写（均需先准备 UTF-8 内容文件）：
- new -File 新文件 -ContentFile c.txt —— 从内容文件创建
- append -File x.cs -ContentFile c.txt —— 追加到末尾
- insert -File x.cs -At 100 -ContentFile c.txt —— 在第 100 行前插入
- replaceline -File x.cs -From 95 -To 105 -ContentFile c.txt —— 整块替换
- deleteline -File x.cs -From 95 -To 105 —— 删除行区间

## 内容文件怎么来（关键步骤）

用 PowerShell 行数组 + 占位符法生成（详见工作区规则 8）：
1. 行数组元素内尖括号写 {LT} {GT}，圈点等特殊符号不写（用纯中文/ASCII 替代）；
2. WriteAllLines 落盘后用 [char]60/[char]62 做 .Replace 二次替换；
3. 小段内容（几行）可直接 PowerShell 行数组写目标文件；本工具价值在大段内容/整文件/行级精确编辑 + 自动验证。

## 标准工作流（写代码文件）

1. 定位：findstr /S /N 关键词 文件，或本工具 show 查看上下文；
2. 准备内容文件（占位符法）；
3. edit_file.ps1 执行 insert / replaceline / new / append / deleteline；
4. 立即编译验证（.cs 文件）；
5. 用完删除内容文件。

## 设计要点

- 编码：读目标文件 BOM 自动探测，写回保持一致；内容文件按 UTF-8；
- 验证：写后 200ms 回读行数+抽查，失败报错退出非 0；
- 单文件单操作：一次命令只改一个文件，损坏影响面可控；
- 与 skill-replace-search 互补：那个做搜索/字面量替换，这个做行级编辑/新建。
