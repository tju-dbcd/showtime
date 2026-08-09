import request from './request'

/**
 * 给指定演出添加场次
 * @param showId 演出ID
 * @param data 场次信息
 */
export const addSession = (showId: number | string, data: {
  startTime: string
  endTime: string
  saleStartTime: string
  saleEndTime: string
  seatMapId: number | string
}) => {
  return request.post(`/api/admin/shows/${showId}/sessions`, data)
}

/**
 * 给场次添加定价策略
 * @param sessionId 场次ID
 * @param data 定价信息
 */
export const addPricingStrategy = (sessionId: number | string, data: {
  seatSectionId: number | null
  strategyName: string
  priceType: string
  saleStartTime: string | null
  saleEndTime: string | null
  priority: number
  quota: number | null
}) => {
  return request.post(`/api/admin/sessions/${sessionId}/pricing-strategies`, data)
}

/**
 * 更新场次状态
 * @param sessionId 场次ID
 * @param status 状态值
 */
export const updateSessionStatus = (sessionId: number | string, status: string) => {
  return request.put(`/api/admin/sessions/${sessionId}/status`, { status })
}
