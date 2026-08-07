// src/context/UserContext.tsx
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
  const [user, setUser] = useState<User>(mockUser);

  const updateUser = (newUser: Partial<User>) => {
    setUser((prev) => ({ ...prev, ...newUser }));
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
