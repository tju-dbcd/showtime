import React, { createContext, useState, useContext } from 'react';
import type { ReactNode } from 'react';

export interface User {
  id: number;
  username: string;
  nickname: string;
  avatar: string;
  phone: string;
  email: string;
  roles: string[];
}

const emptyUser: User = {
  id: 0,
  username: '',
  nickname: '',
  avatar: '',
  phone: '',
  email: '',
  roles: [],
};

interface UserContextType {
  user: User;
  updateUser: (newUser: Partial<User>) => void;
}

const UserContext = createContext<UserContextType | undefined>(undefined);

export const UserProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const getInitialUser = (): User => {
    const stored = localStorage.getItem('user');
    if (!stored) return emptyUser;

    try {
      const parsed = JSON.parse(stored);
      return {
        ...emptyUser,
        ...parsed,
        id: Number(parsed.userId ?? parsed.id ?? 0),
        username: parsed.userName ?? parsed.username ?? '',
        avatar: parsed.avatarUrl ?? parsed.avatar ?? '',
        roles: Array.isArray(parsed.roles) ? parsed.roles.map(String) : [],
      };
    } catch {
      return emptyUser;
    }
  };

  const [user, setUser] = useState<User>(getInitialUser());

  const updateUser = (newUser: Partial<User>) => {
    setUser((prev) => ({ ...prev, ...newUser }));
    // 同步更新 localStorage
    const current = JSON.parse(localStorage.getItem('user') || '{}');
    localStorage.setItem('user', JSON.stringify({ ...current, ...newUser }));
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
