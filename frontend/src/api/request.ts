import axios from 'axios'
import { message } from 'antd'

// 创建axios实例
const request = axios.create({
  baseURL: 'http://120.27.157.163:5146', // 后端地址
  timeout: 10000, // 这里预设超时时间10秒
})

// 请求拦截器（有登录的话，现在先留着）
request.interceptors.request.use(
  (config) => {
    // 如果以后有登录token就在这里加
    // const token = localStorage.getItem('token')
    // if (token) {
    //   config.headers.Authorization = `Bearer ${token}`
    // }
    return config
  },
  (error) => {
    return Promise.reject(error)
  }
)

// 响应拦截器，统一处理返回结果和错误
request.interceptors.response.use(
  (response) => {
    // 直接返回数据部分
    return response.data
  },
  (error) => {
    // 统一错误提示
    message.error(error.response?.data?.message || '请求失败，请重试')
    return Promise.reject(error)
  }
)

export default request