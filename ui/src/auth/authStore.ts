// Auth token storage (localStorage-backed) with a tiny pub/sub for React.
// Tokens are also exposed via getters used by the API client.

const ACCESS_KEY = "cove_access_token";
const REFRESH_KEY = "cove_refresh_token";
const USER_KEY = "cove_user";

export interface AuthUser {
  id: string;
  username: string;
  permissions: string[];
}

type Listener = () => void;
const listeners = new Set<Listener>();

function emit() { for (const l of listeners) l(); }

export const authStore = {
  getAccessToken(): string | null {
    try { return localStorage.getItem(ACCESS_KEY); } catch { return null; }
  },
  getRefreshToken(): string | null {
    try { return localStorage.getItem(REFRESH_KEY); } catch { return null; }
  },
  getUser(): AuthUser | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) as AuthUser : null;
    } catch { return null; }
  },
  setTokens(access: string | null, refresh: string | null) {
    try {
      if (access) localStorage.setItem(ACCESS_KEY, access); else localStorage.removeItem(ACCESS_KEY);
      if (refresh) localStorage.setItem(REFRESH_KEY, refresh); else localStorage.removeItem(REFRESH_KEY);
    } catch { /* ignore */ }
    emit();
  },
  setUser(user: AuthUser | null) {
    try {
      if (user) localStorage.setItem(USER_KEY, JSON.stringify(user)); else localStorage.removeItem(USER_KEY);
    } catch { /* ignore */ }
    emit();
  },
  clear() {
    try {
      localStorage.removeItem(ACCESS_KEY);
      localStorage.removeItem(REFRESH_KEY);
      localStorage.removeItem(USER_KEY);
    } catch { /* ignore */ }
    emit();
  },
  subscribe(fn: Listener): () => void {
    listeners.add(fn);
    return () => { listeners.delete(fn); };
  },
};

// Wildcard-aware permission check matching the server's CovePrincipal.Has().
export function hasPermission(perms: string[] | undefined | null, key: string): boolean {
  if (!perms || perms.length === 0) return false;
  if (perms.includes("*")) return true;
  if (perms.includes(key)) return true;
  const dot = key.indexOf(".");
  if (dot < 0) return false;
  const resource = key.slice(0, dot);
  const verb = key.slice(dot + 1);
  if (perms.includes(`${resource}.*`)) return true;
  if (perms.includes(`*.${verb}`)) return true;
  return false;
}
