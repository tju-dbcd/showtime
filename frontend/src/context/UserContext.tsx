import React, { createContext, useState, useContext } from 'react';
import type { ReactNode } from 'react';
import { mockUser } from '@/mock/user';
import type { User } from '@/mock/user';

interface UserContextType {
  user: User;
  updateUser: (newUser: Partial<User>) => void;
}

const UserContext = createContext<UserContextType | undefined>(undefined);

export const UserProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  // 尝试从 localStorage 读取用户信息
  const getInitialUser = (): User => {
    const stored = localStorage.getItem('user');
    if (stored) {
      try {
        const parsed = JSON.parse(stored);
        // 如果后端返回的是 userName，映射到 username；avatarUrl 映射到 avatar
        if (parsed.userName && !parsed.username) {
          parsed.username = parsed.userName;
        }
        if (parsed.avatarUrl && !parsed.avatar) {
          parsed.avatar = parsed.avatarUrl;
        }
        return { ...mockUser, ...parsed };
      } catch {
        return mockUser;
      }
    }
    return mockUser;
  };

  const [user, setUser] = useState<User>(getInitialUser());

  const updateUser = (newUser: Partial<User>) => {
    setUser((prev) => ({ ...prev, ...newUser }));
    // 同步更新 localStorage
    const current = JSON.parse(localStorage.getItem('user') || '{}');
    localStorage.setItem('user', JSON.stringify({ ...current, ...newUser }));
    // 也更新 mockUser（让其他地方引用 mockUser 的也能拿到最新数据）
    Object.assign(mockUser, newUser);
  };

  return (
    <UserContext.Provider value={{ user, updateUser }}>
      {children}
    </UserContext.Provider>
  );
};

export const useUser = () => {
  const context = useContext(UserContext);
  if (!context) {
    throw new Error('useUser must be used within a UserProvider');
  }
  return context;
};
