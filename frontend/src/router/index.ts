import { createRouter, createWebHistory } from 'vue-router'
import { canAccessScan, shouldRedirectFromLogin } from '../services/sessionPolicy'

const LoginView = () => import('../views/LoginView.vue')
const ScanView = () => import('../views/ScanView.vue')

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/scan',
    },
    {
      path: '/login',
      name: 'login',
      component: LoginView,
      meta: { guestOnly: true },
    },
    {
      path: '/scan',
      name: 'scan',
      component: ScanView,
      meta: { requiresAuth: true },
    },
  ],
})

router.beforeEach((to) => {
  if (to.meta.requiresAuth && !canAccessScan()) {
    return { name: 'login' }
  }

  if (to.meta.guestOnly && shouldRedirectFromLogin()) {
    return { name: 'scan' }
  }

  return true
})

export default router
