# SWCS 局域网扫码查价系统

基于 `总文档.md` 实现的门店 LAN 实时扫码查价项目：

- 后端：`.NET 8 Web API`（只读 SQL Server + JWT）
- 前端：`Vue 3 + Vite + html5-qrcode`（移动端 H5 扫码）
- 部署：`HTTPS`（自签名证书）+ Windows 本机部署脚本

## 目录说明

- `backend/`：API 服务与单元测试
- `frontend/`：移动端网页
- `ops/sql/`：数据库恢复与只读账号脚本
- `ops/ps/`：证书、防火墙、启动脚本
- `docs/`：部署文档

## 快速开始（开发机）

1. 后端构建与测试

```powershell
dotnet build SwcsScanner.sln
dotnet test SwcsScanner.sln --no-build --blame-hang-timeout 60s
```

2. 前端构建

```powershell
cd frontend
npm install
npm run build
```

3. 启动（前端构建后复制到后端 `wwwroot` 并运行 API）

```powershell
cd ops/ps
.\start-services.ps1 -BuildFrontend
```

生产环境（数据库已存在，推荐）可使用一键检查+启动：

```powershell
cd ops/ps
.\init-and-start.ps1 -BuildFrontend -Environment Production
```

## 数据库恢复脚本

按你的要求提供原始恢复脚本：`ops/sql/restore_swcs.sql`

```sql
RESTORE DATABASE swcs FROM DISK = 'F:\swcs_backup.bak' WITH REPLACE;
```

如需指定数据文件落地路径，请使用 `ops/sql/restore_swcs_with_move.sql`。

## API 概览

- `POST /api/auth/login`：登录换取 JWT
- `GET /api/products/lookup?barcode=xxx`：扫码查价
- `GET /api/products/search?keyword=6901&limit=20`：条码片段模糊查询（前缀优先，包含兜底）
- `GET /api/health`：健康检查

## 默认账号与配置

- 默认账号：`user01 / 1234`
- 生产配置：`backend/src/SwcsScanner.Api/appsettings.Production.json`
- 必改项：SQL 连接串、JWT 密钥、证书密码
