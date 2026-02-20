# 部署手册（Windows + SQL Server + 局域网手机）

## 1. 恢复数据库

在 SQL Server Management Studio 中执行：

```sql
USE [master];
GO
RESTORE DATABASE swcs FROM DISK = 'F:\swcs_backup.bak' WITH REPLACE;
GO
```

脚本文件：`ops/sql/restore_swcs.sql`

如果报逻辑文件名冲突，改用 `ops/sql/restore_swcs_with_move.sql`。

## 2. 创建只读账号

执行脚本：`ops/sql/create_readonly_user.sql`

会创建 `swcs_reader`，并授予 `db_datareader`，同时拒绝写操作权限。

## 3. 生成 HTTPS 自签名证书

在管理员 PowerShell 中执行：

```powershell
cd ops/ps
.\new-self-signed-cert.ps1 -Hostname "管家婆主机名" -IpAddress "192.168.1.100"
```

输出文件位于：`backend/src/SwcsScanner.Api/certs/`

- `swcs-scanner.pfx`：后端 Kestrel 使用
- `swcs-scanner.cer`：导入手机并信任

## 4. 配置后端

编辑 `backend/src/SwcsScanner.Api/appsettings.Production.json`：

1. `ConnectionStrings:SwcsReadonly`
2. `Auth:Users`
3. `Jwt:Key`
4. `Https:PfxPath` 和 `Https:PfxPassword`

## 5. 放行防火墙端口

```powershell
cd ops/ps
.\open-firewall-port.ps1 -Port 5001
```

## 6. 构建并启动服务

```powershell
cd ops/ps
.\start-services.ps1 -BuildFrontend -Environment Production
```

默认访问地址：

- 手机端：`https://<主机局域网IP>:5001`
- 健康检查：`https://<主机局域网IP>:5001/api/health`

## 7. 手机端证书信任提示

1. 将 `swcs-scanner.cer` 发送到手机。
2. 安装证书。
3. 在系统“受信任证书”中启用该证书。
4. 再访问 `https://<主机IP>:5001`，允许继续访问。

## 8. 常见问题

1. 手机无法调起摄像头：确认是 `HTTPS`，不是 `HTTP`。
2. 手机无法访问服务：确认主机静态 IP、同一 WiFi、5001 端口已放行。
3. 登录成功但查不到商品：在 SSMS 检查 `Ptype` 实际字段名，调整 `Swcs:BarcodeFields` 和 `Swcs:PriceFields`。
