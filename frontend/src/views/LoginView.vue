<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { isAxiosError } from 'axios'
import { api } from '../services/api'
import { saveSession } from '../services/auth'
import type { ApiEnvelope, LoginResponse } from '../types/api'

const router = useRouter()
const username = ref('user01')
const password = ref('')
const loading = ref(false)
const errorMessage = ref('')

async function submitLogin(): Promise<void> {
  errorMessage.value = ''
  if (!username.value.trim() || !password.value.trim()) {
    errorMessage.value = '请输入用户名和密码。'
    return
  }

  loading.value = true
  try {
    const response = await api.post<ApiEnvelope<LoginResponse>>('/api/v2/auth/login', {
      username: username.value.trim(),
      password: password.value,
    })

    if (!response.data.data) {
      errorMessage.value = '登录失败，请稍后重试。'
      return
    }

    saveSession(response.data.data.accessToken, response.data.data.expiresAtUtc, username.value.trim())
    await router.replace({ name: 'scan' })
  } catch (error) {
    if (isAxiosError<ApiEnvelope<unknown>>(error) && error.response?.data?.message) {
      errorMessage.value = error.response.data.message
    } else {
      errorMessage.value = '登录失败，请检查网络连接。'
    }
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <main class="login-page">
    <section class="login-card">
      <p class="login-kicker">SWCS LAN</p>
      <h1 class="login-title">扫码查价登录</h1>
      <p class="login-subtitle">连接店内 WiFi 后登录，进入实时扫码查价。</p>

      <form class="login-form" @submit.prevent="submitLogin">
        <label class="login-label" for="username">账号</label>
        <input id="username" v-model="username" class="login-input" maxlength="64" autocomplete="username" />

        <label class="login-label" for="password">密码</label>
        <input
          id="password"
          v-model="password"
          class="login-input"
          type="password"
          maxlength="128"
          autocomplete="current-password"
        />

        <p v-if="errorMessage" class="login-error">{{ errorMessage }}</p>

        <button class="login-submit" type="submit" :disabled="loading">
          {{ loading ? '登录中...' : '登录并开始扫码' }}
        </button>
      </form>
    </section>
  </main>
</template>
