import axios from 'axios'
import { clearSession, getAccessToken } from './auth'

const baseURL = 'https://192.168.0.188:5001'

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
      window.location.replace('/login')
    }

    return Promise.reject(error)
  },
)
