import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { authStore, hasPermission as hasPermImpl } from "./authStore";
import type { AuthUser } from "./authStore";

interface AuthState {
  user: AuthUser | null;
  permissions: string[];
  loading: boolean;
  authEnabled: boolean;
  /** Whether the current user is authenticated AND has loaded from /me. */
  ready: boolean;
}

interface AuthContextValue extends AuthState {
  login(username: string, password: string): Promise<{ ok: boolean; error?: string }>;
  logout(): Promise<void>;
  hasPermission(key: string): boolean;
  refreshMe(): Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

interface MeResponse {
  user: { id: string; username: string; roles?: string[] };
  permissions: string[];
}

interface LoginResponse {
  token: string;
  refreshToken: string;
  user?: { id: number | string; username: string };
  username?: string;
}

async function fetchMe(): Promise<MeResponse | null> {
  const token = authStore.getAccessToken();
  if (!token) return null;
  const res = await fetch("/api/auth/me", { headers: { Authorization: `Bearer ${token}` } });
  if (!res.ok) return null;
  return res.json() as Promise<MeResponse>;
}

export function AuthProvider({ children, authEnabled }: { children: ReactNode; authEnabled: boolean }) {
  const [user, setUser] = useState<AuthUser | null>(() => authStore.getUser());
  const [loading, setLoading] = useState(true);

  const refreshMe = useCallback(async () => {
    const me = await fetchMe();
    if (me) {
      const u: AuthUser = { id: String(me.user.id), username: me.user.username, permissions: me.permissions };
      authStore.setUser(u);
      setUser(u);
    } else {
      authStore.clear();
      setUser(null);
    }
  }, []);

  // On mount: if a token exists, validate it via /me. Subscribe to store changes too.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!authEnabled) { setLoading(false); return; }
      if (authStore.getAccessToken()) {
        await refreshMe();
      }
      if (!cancelled) setLoading(false);
    })();
    const unsub = authStore.subscribe(() => {
      setUser(authStore.getUser());
    });
    return () => { cancelled = true; unsub(); };
  }, [authEnabled, refreshMe]);

  // Listen for global "auth required" events (from authedFetch on hard 401)
  useEffect(() => {
    const handler = () => {
      authStore.clear();
      setUser(null);
    };
    window.addEventListener("cove-auth-required", handler);
    return () => window.removeEventListener("cove-auth-required", handler);
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const res = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username, password }),
    });
    if (!res.ok) {
      let message = "Invalid credentials.";
      try {
        const body = await res.json() as { message?: string };
        if (body?.message) message = body.message;
      } catch { /* ignore */ }
      return { ok: false, error: message };
    }
    const body = await res.json() as LoginResponse;
    authStore.setTokens(body.token, body.refreshToken);
    await refreshMe();
    return { ok: true };
  }, [refreshMe]);

  const logout = useCallback(async () => {
    const refresh = authStore.getRefreshToken();
    try {
      await fetch("/api/auth/logout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: refresh ?? "" }),
      });
    } catch { /* ignore */ }
    authStore.clear();
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    permissions: user?.permissions ?? [],
    loading,
    authEnabled,
    ready: !authEnabled || !!user,
    login,
    logout,
    hasPermission: (k: string) => hasPermImpl(user?.permissions, k),
    refreshMe,
  }), [user, loading, authEnabled, login, logout, refreshMe]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}

/** Conditionally renders children only when the current user has the given permission. */
export function RequirePermission({ perm, fallback = null, children }: { perm: string; fallback?: ReactNode; children: ReactNode }) {
  const { hasPermission } = useAuth();
  return hasPermission(perm) ? <>{children}</> : <>{fallback}</>;
}
