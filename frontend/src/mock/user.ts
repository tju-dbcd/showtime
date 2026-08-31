export interface User {
  id: number;
  username: string;
  nickname: string;
  avatar: string;
  phone: string;
  email: string;
  realName: string;
  idCard: string;
  isVerified: boolean; // 是否实名认证
  addressList: Address[];
}

export interface Address {
  id: number;
  name: string;
  phone: string;
  province: string;
  city: string;
  district: string;
  detail: string;
  isDefault: boolean;
}

export const mockUser: User = {
  id: 1,
  username: 'Creeperwww',
  nickname: '小周',
  avatar: 'https://picsum.photos/seed/user/200/200',
  phone: '138****8888',
  email: '957****515@qq.com',
  realName: '周杰伦',
  idCard: '110101199001011234',
  isVerified: true,
  addressList: [
    {
      id: 1,
      name: '周杰伦',
      phone: '13812345678',
      province: '上海市',
      city: '上海市',
      district: '浦东新区',
      detail: '陆家嘴环路1000号恒生银行大厦18楼',
      isDefault: true,
    },
    {
      id: 2,
      name: '周杰伦',
      phone: '13912345678',
      province: '北京市',
      city: '北京市',
      district: '朝阳区',
      detail: '建国门外大街1号国贸大厦A座2301',
      isDefault: false,
    },
  ],
};
