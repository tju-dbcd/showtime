// src/mock/events.ts
export interface Event {
  id: number;
  name: string;
  venue: string;
  date: string;
  price: number;
  img: string;
  category: '演唱会' | '话剧' | '音乐剧' | '体育';
  city: '北京' | '上海' | '广州' | '成都' | '杭州' | '南京';
  description: string;      // 演出简介
  images: string[];         // 更多宣传图
  duration: string;         // 演出时长
  notice: string;           // 购票须知
}

export const mockEvents: Event[] = [
  {
    id: 1,
    name: '周杰伦 2026 嘉年华世界巡回演唱会',
    venue: '上海体育场',
    date: '2026-12-31 19:30',
    price: 580,
    img: 'https://picsum.photos/seed/jay/600/400',
    category: '演唱会',
    city: '上海',
    description: '周杰伦「嘉年华」世界巡回演唱会，用音乐带领观众穿梭于梦幻与现实之间。全新的舞台设计、震撼的视听效果，以及周杰伦经典曲目与新歌的完美融合，将为歌迷带来一场难忘的音乐盛宴。',
    images: [
      'https://picsum.photos/seed/jay1/800/400',
      'https://picsum.photos/seed/jay2/800/400',
      'https://picsum.photos/seed/jay3/800/400',
    ],
    duration: '约 150 分钟（含中场休息）',
    notice: '1. 本场演出需实名购票，一票一证。\n2. 演出票品为有价证券，非普通商品，其背后承载的文化服务具有时效性、稀缺性等特征，不支持退换。\n3. 请于演出前至少 1 小时到场，配合安检。',
  },
  {
    id: 2,
    name: '林俊杰 JJ20 世界巡回演唱会',
    venue: '北京国家体育场（鸟巢）',
    date: '2026-11-15 19:00',
    price: 880,
    img: 'https://picsum.photos/seed/jj/600/400',
    category: '演唱会',
    city: '北京',
    description: '林俊杰 JJ20 世界巡回演唱会，庆祝出道 20 周年。从《江南》到《交换余生》，林俊杰将用歌声串联起 20 年的音乐旅程，带观众重温那些感动过无数人的经典旋律。',
    images: [
      'https://picsum.photos/seed/jj1/800/400',
      'https://picsum.photos/seed/jj2/800/400',
    ],
    duration: '约 140 分钟',
    notice: '1. 本场演出需实名购票。\n2. 演出票品不支持退换。\n3. 请于演出前 1.5 小时到场。',
  },
  {
    id: 3,
    name: '话剧《如梦之梦》经典版',
    venue: '上海文化广场',
    date: '2026-10-20 19:15',
    price: 680,
    img: 'https://picsum.photos/seed/dream/600/400',
    category: '话剧',
    city: '上海',
    description: '赖声川导演经典话剧《如梦之梦》再度上演。八小时史诗级演出，带你走进一场关于生命、爱情与轮回的梦境。明星阵容倾情演绎，不容错过。',
    images: [
      'https://picsum.photos/seed/dream1/800/400',
      'https://picsum.photos/seed/dream2/800/400',
    ],
    duration: '约 480 分钟（含两场，中场休息各 20 分钟）',
    notice: '1. 本剧分上下两场，请留意购票场次。\n2. 演出票品不支持退换。\n3. 迟到观众需在幕间入场。',
  },
  {
    id: 4,
    name: '薛之谦 天外来物 巡回演唱会',
    venue: '南京奥体中心',
    date: '2026-09-10 19:30',
    price: 780,
    img: 'https://picsum.photos/seed/xue/600/400',
    category: '演唱会',
    city: '南京',
    description: '薛之谦「天外来物」巡回演唱会，以科幻概念打造沉浸式舞台体验。薛之谦将带来《演员》《丑八怪》《天外来物》等热门歌曲，与观众共同探索音乐与宇宙的交汇。',
    images: [
      'https://picsum.photos/seed/xue1/800/400',
      'https://picsum.photos/seed/xue2/800/400',
    ],
    duration: '约 140 分钟',
    notice: '1. 本场演出需实名购票。\n2. 演出票品不支持退换。\n3. 现场禁止携带专业摄影器材。',
  },
  {
    id: 5,
    name: '音乐剧《剧院魅影》中文版',
    venue: '广州大剧院',
    date: '2026-08-25 19:00',
    price: 980,
    img: 'https://picsum.photos/seed/phantom/600/400',
    category: '音乐剧',
    city: '广州',
    description: '韦伯经典音乐剧《剧院魅影》中文版登陆广州。华丽的舞美、动人的旋律、扣人心弦的剧情，中文版由顶尖音乐剧演员倾情演绎，再现巴黎歌剧院的传奇故事。',
    images: [
      'https://picsum.photos/seed/phantom1/800/400',
      'https://picsum.photos/seed/phantom2/800/400',
    ],
    duration: '约 155 分钟（含中场休息）',
    notice: '1. 本场演出需实名购票。\n2. 演出票品不支持退换。\n3. 建议提前 30 分钟到场。',
  },
  {
    id: 6,
    name: '陈奕迅 FEAR AND DREAMS 演唱会',
    venue: '杭州奥体中心',
    date: '2026-07-18 19:30',
    price: 680,
    img: 'https://picsum.photos/seed/eason/600/400',
    category: '演唱会',
    city: '杭州',
    description: '陈奕迅「FEAR AND DREAMS」演唱会，以恐惧与梦想为主题，通过音乐探讨人性的深处。陈奕迅将用他独特的嗓音和舞台魅力，带领观众经历一场情感与思考的旅程。',
    images: [
      'https://picsum.photos/seed/eason1/800/400',
      'https://picsum.photos/seed/eason2/800/400',
    ],
    duration: '约 150 分钟',
    notice: '1. 本场演出需实名购票。\n2. 演出票品不支持退换。\n3. 请于演出前 1 小时到场。',
  },
  {
    id: 7,
    name: 'CBA 全明星周末',
    venue: '北京五棵松体育馆',
    date: '2026-12-01 19:00',
    price: 380,
    img: 'https://picsum.photos/seed/cba/600/400',
    category: '体育',
    city: '北京',
    description: 'CBA 全明星周末，汇集中国篮球最顶尖的球星。扣篮大赛、三分大赛、全明星正赛，精彩不断。感受篮球的魅力，见证明星球员的精彩表现。',
    images: [
      'https://picsum.photos/seed/cba1/800/400',
      'https://picsum.photos/seed/cba2/800/400',
    ],
    duration: '约 180 分钟',
    notice: '1. 本场赛事需实名购票。\n2. 门票不支持退换。\n3. 请勿携带违禁品入场。',
  },
  {
    id: 8,
    name: '话剧《雷雨》特别纪念版',
    venue: '北京人艺小剧场',
    date: '2026-09-15 19:30',
    price: 480,
    img: 'https://picsum.photos/seed/leiyu/600/400',
    category: '话剧',
    city: '北京',
    description: '曹禺经典话剧《雷雨》特别纪念版，北京人艺实力阵容倾情呈现。在雷雨交加的夜晚，一个家族的秘密与悲剧缓缓揭开，感受中国话剧史上最伟大的作品之一。',
    images: [
      'https://picsum.photos/seed/leiyu1/800/400',
      'https://picsum.photos/seed/leiyu2/800/400',
    ],
    duration: '约 150 分钟（含中场休息）',
    notice: '1. 本场演出需实名购票。\n2. 演出票品不支持退换。\n3. 建议提前 30 分钟到场。',
  },
];
