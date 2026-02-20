# SWCS 扫码前端（Vue 3 + Vite）

## 开发

```powershell
npm install
npm run dev
```

## 构建

```powershell
npm run build
```

构建产物在 `dist/`，可由 `ops/ps/start-services.ps1` 自动复制到后端 `wwwroot/`。

## 环境变量

复制 `.env.example` 为 `.env.local` 后修改：

- `VITE_API_BASE_URL`：API 基础地址
- `VITE_PROXY_TARGET`：本地开发代理目标地址
