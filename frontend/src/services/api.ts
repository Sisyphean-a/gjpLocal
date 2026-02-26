import axios from 'axios'
import { clearSession, getAccessToken } from './auth'
import { isAuthRequired } from './sessionPolicy'

const baseURL = (import.meta.env.VITE_API_BASE_URL ?? '').trim()

export const api = axios.create({
  baseURL,
  timeout: 2500,
})

api.interceptors.request.use((config) => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }

  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearSession()
      if (isAuthRequired() && typeof window !== 'undefined' && window.location.pathname !== '/login') {
        window.location.replace('/login')
      }
    }

    return Promise.reject(error)
  },
)
