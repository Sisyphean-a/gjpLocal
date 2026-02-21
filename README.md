# SWCS 局域网扫码查价系统

本项目用于门店局域网扫码查价，前端为移动端 H5，后端为 .NET 8 API。

## 当前系统口径（已对齐）

- 数据库：`dwpfbcs`
- 商品名称：返回 `pfullname`（商品全名）
- 价格：按扫码命中的条码 `UnitID` + `PriceTypeId=0001`（零售价）取价

## 目录

- `backend/` 后端 API
- `frontend/` 前端页面
- `ops/ps/` 启动、证书、防火墙脚本
- `start-all.bat` 一键启动脚本（本次新增）

## 一键启动（推荐）

首次启动（会重建前端并启动后端）：

```bat
start-all.bat
```

日常启动（不重建前端，更快）：

```bat
start-all.bat nobuild
```

启动后访问：

- 本机：`https://localhost:5001`
- 手机：`https://<本机局域网IP>:5001`

默认账号：`user01 / 1234`

## 开发模式（手动）

前端开发：

```powershell
cd frontend
npm install
npm run dev
```

后端开发：

```powershell
$env:ASPNETCORE_ENVIRONMENT='Production'
dotnet run --project backend/src/SwcsScanner.Api/SwcsScanner.Api.csproj --configuration Release --no-launch-profile
```

## 后端是否要每次开机手动启动？

- 默认是：需要运行一次启动命令（或 `start-all.bat`）。
- 可以改造成开机自启：见 `目标电脑运行指南.md` 的“开机自启/服务化”章节。

## 开机自启服务（已支持）

管理员终端执行：

```bat
install-service.bat
```

或直接执行：

```powershell
.\ops\ps\install-windows-service.ps1 -BuildFrontend -Environment Production
```

卸载服务：

```bat
uninstall-service.bat
```

说明：

- 服务名：`SwcsScanner`
- 默认发布目录：`C:\Services\SwcsScanner`
- 启动类型：`Automatic`（开机自启）

## 服务化后能否删除当前项目目录？

- 只要服务安装成功且你不再需要“二次构建/升级”，**可以删除** `E:\gjpLocal`。
- 原因：服务实际运行的是 `C:\Services\SwcsScanner` 下发布后的可执行文件，不依赖源码目录。
- 建议保留：
  - `C:\Services\SwcsScanner`（服务运行目录）
  - 当前仓库副本（后续升级更方便）
