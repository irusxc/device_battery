# 发布到 GitHub

仓库用于保存源码、构建脚本、文档和演示截图；Releases 用于提供可直接运行的 ZIP。源码采用现有 MIT 许可证。

## 创建远程仓库

在 GitHub 右上角选择 **New repository**：

- Repository name：可使用 `peripheral-power` 或 `device-battery`。
- Description：`Windows USB 设备连接状态与外设电量托盘工具`。
- Visibility：希望所有人查看和下载时选择 Public；仅分享给指定协作者时选择 Private。
- 本地已有 README、LICENSE 和 Git 历史，因此创建空仓库，不勾选初始化 README、.gitignore 或 License。

仓库名只是建议，使用实际创建的地址关联本地仓库。

## 上传现有项目

当前本地已有 Git 仓库，分支名为 `master`，无需再次执行 `git init`。

可以用 GitHub Desktop：**File → Add local repository**，选择本项目目录，查看 Changes、填写提交说明并 Commit，然后 **Publish repository**。发布窗口中的仓库名称和可见性应按你的意愿设置。采用 GitHub Desktop 直接创建远程仓库时，不必先在网页创建同名仓库。

也可以在项目目录终端执行以下命令。下面使用当前项目的 GitHub 地址；如果已有 `origin`，跳过添加远程的那一行：

```powershell
git status
git add -A
git commit -m "Release 0.2.0: fix USB hotplug updates and simplify UI"
git remote add origin https://github.com/irusxc/device_battery.git
git push -u origin master
```

这些命令将当前工作区的修改提交并推送到远程。上传前查看 `git status`，确认要包含的文件。若之后已配置 `origin`，不必重复 `git remote add`，使用 `git remote -v` 查看现有地址。

`.gitignore` 已排除 `dist/`、`build/`、本地参考下载、日志、EXE 和 ZIP。现有旧版 ZIP 已从工作区删除，这次提交会记录删除；它仍可能保留在旧提交历史中。不要使用 `git add -f` 把新版发布包强行加入源码仓库。

## 发布可下载版本

1. 确认最新源码已推送，构建和验证已通过。
2. 在 GitHub 仓库右侧打开 **Releases → Draft a new release**。
3. 创建标签 `v0.2.0`，Target 选择包含新版源码的 `master` 分支或对应提交。
4. Release title 填写 `栖电 0.2.0`。
5. 将 `docs/RELEASE-v0.2.0.md` 的内容复制到发布说明。
6. 上传本地 `dist/v0.2.0/PeripheralPower-0.2.0-win-x64.zip`。
7. 建议勾选 **Set as a pre-release**：界面和连接检测已有回归检查，具体无线硬件的电量兼容性仍需实机验证。
8. 确认附件和说明后选择 **Publish release**。朋友从该 Release 的 **Assets** 下载程序 ZIP。

预发布版通常不会作为稳定版的 Latest release 展示，分享该版本的具体 Release 页面链接即可。

## 本地重新生成发布包

在 Windows 开发环境运行：

```powershell
python tools/build.py
python tools/verify.py
python tools/check_runtime.py
python tools/finalize.py
```

最终包位于 `dist/v0.2.0/PeripheralPower-0.2.0-win-x64.zip`。`verify.py` 会扫描本机设备并在 `build/` 生成本地诊断和截图；这些文件不提交。`finalize.py` 会更新 `docs/` 中的预览图并打包；提交前查看预览图和文档内容。

发布前应明确区分设备连接检测和电量支持。不要将“可发现 USB 设备”描述为“所有 USB / 2.4G 设备均可读取实时电量”。
