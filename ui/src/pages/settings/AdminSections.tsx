import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { auditApi, rolesApi, usersApi, type PermissionInfo, type RoleRow, type UserRow } from "../../api/client";
import { useAuth } from "../../auth/AuthContext";

function Section({ title, description, children, actions }: { title: string; description?: string; children: React.ReactNode; actions?: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-app bg-surface p-5 shadow-sm">
      <header className="mb-4 flex items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold">{title}</h2>
          {description ? <p className="mt-1 text-sm text-secondary">{description}</p> : null}
        </div>
        {actions}
      </header>
      {children}
    </section>
  );
}

function Btn(props: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "danger" | "ghost" }) {
  const { variant = "ghost", className = "", ...rest } = props;
  const base = "inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition";
  const v =
    variant === "primary" ? "bg-blue-600 text-white hover:bg-blue-500" :
    variant === "danger" ? "bg-red-600 text-white hover:bg-red-500" :
    "border border-app bg-surface-2 hover:bg-surface-3";
  return <button {...rest} className={`${base} ${v} ${className}`} />;
}

// =========================================================================
// USERS
// =========================================================================
export function UsersTab() {
  const auth = useAuth();
  const qc = useQueryClient();
  const usersQ = useQuery({ queryKey: ["admin", "users"], queryFn: usersApi.list });
  const rolesQ = useQuery({ queryKey: ["admin", "roles"], queryFn: rolesApi.list });

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<UserRow | null>(null);
  const [pwUser, setPwUser] = useState<UserRow | null>(null);

  const removeM = useMutation({
    mutationFn: (id: number) => usersApi.remove(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["admin", "users"] }),
  });
  const unlockM = useMutation({
    mutationFn: (id: number) => usersApi.unlock(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["admin", "users"] }),
  });

  return (
    <div className="space-y-6">
      <Section
        title="Users"
        description="Local user accounts and their role assignments."
        actions={auth.hasPermission("users.create") ? <Btn variant="primary" onClick={() => setCreating(true)}>+ New user</Btn> : null}
      >
        {usersQ.isLoading ? <p className="text-sm text-secondary">Loading…</p> : null}
        {usersQ.error ? <p className="text-sm text-red-400">Failed to load users.</p> : null}
        {usersQ.data ? (
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead className="border-b border-app text-left text-xs uppercase tracking-wide text-secondary">
                <tr>
                  <th className="px-2 py-2">Username</th>
                  <th className="px-2 py-2">Display name</th>
                  <th className="px-2 py-2">Roles</th>
                  <th className="px-2 py-2">Status</th>
                  <th className="px-2 py-2">Last login</th>
                  <th className="px-2 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {usersQ.data.map((u) => (
                  <tr key={u.id} className="border-b border-app/40">
                    <td className="px-2 py-2 font-medium">{u.username}{u.isSystem ? <span className="ml-1 text-xs text-secondary">(system)</span> : null}</td>
                    <td className="px-2 py-2">{u.displayName ?? <span className="text-secondary">—</span>}</td>
                    <td className="px-2 py-2">{u.roles.join(", ") || <span className="text-secondary">—</span>}</td>
                    <td className="px-2 py-2">
                      {u.isLocked ? <span className="text-amber-400">locked</span> :
                       !u.isActive ? <span className="text-secondary">disabled</span> :
                       <span className="text-emerald-400">active</span>}
                    </td>
                    <td className="px-2 py-2 text-secondary">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : "—"}</td>
                    <td className="px-2 py-2 text-right space-x-1">
                      {auth.hasPermission("users.update") ? <Btn onClick={() => setEditing(u)}>Edit</Btn> : null}
                      {auth.hasPermission("users.update") ? <Btn onClick={() => setPwUser(u)}>Password</Btn> : null}
                      {u.isLocked && auth.hasPermission("users.update") ? <Btn onClick={() => unlockM.mutate(u.id)}>Unlock</Btn> : null}
                      {auth.hasPermission("users.delete") && !u.isSystem ? (
                        <Btn variant="danger" onClick={() => { if (confirm(`Delete user "${u.username}"?`)) removeM.mutate(u.id); }}>Delete</Btn>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </Section>

      {creating ? (
        <CreateUserDialog roles={rolesQ.data ?? []} onClose={() => setCreating(false)} />
      ) : null}
      {editing ? (
        <EditUserDialog user={editing} roles={rolesQ.data ?? []} onClose={() => setEditing(null)} />
      ) : null}
      {pwUser ? (
        <PasswordDialog user={pwUser} onClose={() => setPwUser(null)} />
      ) : null}
    </div>
  );
}

function CreateUserDialog({ roles, onClose }: { roles: RoleRow[]; onClose: () => void }) {
  const qc = useQueryClient();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);
  const [mustChange, setMustChange] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const m = useMutation({
    mutationFn: () => usersApi.create({ username, password, displayName: displayName || undefined, email: email || undefined, roles: selectedRoles, mustChangePassword: mustChange }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin", "users"] }); onClose(); },
    onError: (e: any) => setErr(e?.message ?? "Failed"),
  });

  return (
    <Modal title="Create user" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Username"><input className="input" value={username} onChange={(e) => setUsername(e.target.value)} /></Field>
        <Field label="Password"><input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
        <Field label="Display name"><input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Email"><input className="input" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
        <Field label="Roles">
          <div className="flex flex-wrap gap-2">
            {roles.map(r => (
              <label key={r.name} className="inline-flex items-center gap-1.5 rounded border border-app px-2 py-1 text-sm">
                <input type="checkbox" checked={selectedRoles.includes(r.name)} onChange={(e) => setSelectedRoles(s => e.target.checked ? [...s, r.name] : s.filter(x => x !== r.name))} />
                {r.name}
              </label>
            ))}
          </div>
        </Field>
        <label className="inline-flex items-center gap-2 text-sm">
          <input type="checkbox" checked={mustChange} onChange={(e) => setMustChange(e.target.checked)} />
          Force password change at next login
        </label>
        {err ? <p className="text-sm text-red-400">{err}</p> : null}
        <div className="flex justify-end gap-2 pt-2">
          <Btn onClick={onClose}>Cancel</Btn>
          <Btn variant="primary" onClick={() => m.mutate()} disabled={!username || !password || m.isPending}>Create</Btn>
        </div>
      </div>
    </Modal>
  );
}

function EditUserDialog({ user, roles, onClose }: { user: UserRow; roles: RoleRow[]; onClose: () => void }) {
  const qc = useQueryClient();
  const [displayName, setDisplayName] = useState(user.displayName ?? "");
  const [email, setEmail] = useState(user.email ?? "");
  const [isActive, setIsActive] = useState(user.isActive);
  const [selectedRoles, setSelectedRoles] = useState<string[]>(user.roles);
  const [err, setErr] = useState<string | null>(null);

  const updateM = useMutation({
    mutationFn: async () => {
      await usersApi.update(user.id, { displayName: displayName || undefined, email: email || undefined, isActive });
      await usersApi.setRoles(user.id, selectedRoles);
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin", "users"] }); onClose(); },
    onError: (e: any) => setErr(e?.message ?? "Failed"),
  });

  return (
    <Modal title={`Edit ${user.username}`} onClose={onClose}>
      <div className="space-y-3">
        <Field label="Display name"><input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Email"><input className="input" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
        <label className="inline-flex items-center gap-2 text-sm">
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          Active
        </label>
        <Field label="Roles">
          <div className="flex flex-wrap gap-2">
            {roles.map(r => (
              <label key={r.name} className="inline-flex items-center gap-1.5 rounded border border-app px-2 py-1 text-sm">
                <input type="checkbox" checked={selectedRoles.includes(r.name)} disabled={user.isSystem && r.name === "Owner"} onChange={(e) => setSelectedRoles(s => e.target.checked ? [...s, r.name] : s.filter(x => x !== r.name))} />
                {r.name}
              </label>
            ))}
          </div>
        </Field>
        {err ? <p className="text-sm text-red-400">{err}</p> : null}
        <div className="flex justify-end gap-2 pt-2">
          <Btn onClick={onClose}>Cancel</Btn>
          <Btn variant="primary" onClick={() => updateM.mutate()} disabled={updateM.isPending}>Save</Btn>
        </div>
      </div>
    </Modal>
  );
}

function PasswordDialog({ user, onClose }: { user: UserRow; onClose: () => void }) {
  const [password, setPassword] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const m = useMutation({
    mutationFn: () => usersApi.adminChangePassword(user.id, password),
    onSuccess: () => onClose(),
    onError: (e: any) => setErr(e?.message ?? "Failed"),
  });
  return (
    <Modal title={`Change password for ${user.username}`} onClose={onClose}>
      <div className="space-y-3">
        <Field label="New password"><input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
        {err ? <p className="text-sm text-red-400">{err}</p> : null}
        <div className="flex justify-end gap-2 pt-2">
          <Btn onClick={onClose}>Cancel</Btn>
          <Btn variant="primary" onClick={() => m.mutate()} disabled={!password || m.isPending}>Set password</Btn>
        </div>
      </div>
    </Modal>
  );
}

// =========================================================================
// ROLES
// =========================================================================
export function RolesTab() {
  const auth = useAuth();
  const qc = useQueryClient();
  const rolesQ = useQuery({ queryKey: ["admin", "roles"], queryFn: rolesApi.list });
  const permsQ = useQuery({ queryKey: ["admin", "permissions"], queryFn: rolesApi.permissions });

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<RoleRow | null>(null);

  const removeM = useMutation({
    mutationFn: (id: number) => rolesApi.remove(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["admin", "roles"] }),
  });

  return (
    <div className="space-y-6">
      <Section
        title="Roles"
        description="Roles bundle permissions and are assigned to users."
        actions={auth.hasPermission("roles.create") ? <Btn variant="primary" onClick={() => setCreating(true)}>+ New role</Btn> : null}
      >
        {rolesQ.isLoading ? <p className="text-sm text-secondary">Loading…</p> : null}
        {rolesQ.data ? (
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead className="border-b border-app text-left text-xs uppercase tracking-wide text-secondary">
                <tr>
                  <th className="px-2 py-2">Name</th>
                  <th className="px-2 py-2">Description</th>
                  <th className="px-2 py-2">Source</th>
                  <th className="px-2 py-2">Permissions</th>
                  <th className="px-2 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rolesQ.data.map((r) => (
                  <tr key={r.id} className="border-b border-app/40">
                    <td className="px-2 py-2 font-medium">{r.name}{r.isBuiltin ? <span className="ml-1 text-xs text-secondary">(builtin)</span> : null}</td>
                    <td className="px-2 py-2 text-secondary">{r.description ?? "—"}</td>
                    <td className="px-2 py-2 text-secondary">{r.source}</td>
                    <td className="px-2 py-2 text-secondary">{r.permissions.length}</td>
                    <td className="px-2 py-2 text-right space-x-1">
                      {auth.hasPermission("roles.update") ? <Btn onClick={() => setEditing(r)}>{r.isBuiltin ? "View" : "Edit"}</Btn> : null}
                      {auth.hasPermission("roles.delete") && !r.isBuiltin ? (
                        <Btn variant="danger" onClick={() => { if (confirm(`Delete role "${r.name}"?`)) removeM.mutate(r.id); }}>Delete</Btn>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </Section>

      {creating ? <RoleEditor permissions={permsQ.data ?? []} onClose={() => setCreating(false)} /> : null}
      {editing ? <RoleEditor role={editing} permissions={permsQ.data ?? []} onClose={() => setEditing(null)} /> : null}
    </div>
  );
}

function RoleEditor({ role, permissions, onClose }: { role?: RoleRow; permissions: PermissionInfo[]; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(role?.name ?? "");
  const [description, setDescription] = useState(role?.description ?? "");
  const [perms, setPerms] = useState<string[]>(role?.permissions ?? []);
  const [err, setErr] = useState<string | null>(null);
  const isReadOnly = !!role?.isBuiltin;

  const grouped = useMemo(() => {
    const m = new Map<string, PermissionInfo[]>();
    for (const p of permissions) {
      const arr = m.get(p.category) ?? [];
      arr.push(p);
      m.set(p.category, arr);
    }
    return Array.from(m.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [permissions]);

  const m = useMutation({
    mutationFn: () => role
      ? rolesApi.update(role.id, { description: description || undefined, permissions: perms })
      : rolesApi.create({ name, description: description || undefined, permissions: perms }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["admin", "roles"] }); onClose(); },
    onError: (e: any) => setErr(e?.message ?? "Failed"),
  });

  return (
    <Modal title={role ? `${isReadOnly ? "View" : "Edit"} role: ${role.name}` : "New role"} onClose={onClose} wide>
      <div className="space-y-3">
        {!role ? <Field label="Name"><input className="input" value={name} onChange={(e) => setName(e.target.value)} /></Field> : null}
        <Field label="Description"><input className="input" value={description} onChange={(e) => setDescription(e.target.value)} disabled={isReadOnly} /></Field>
        <Field label={`Permissions (${perms.length} selected)`}>
          <div className="max-h-96 overflow-auto rounded border border-app p-2 space-y-3">
            {grouped.map(([cat, list]) => (
              <div key={cat}>
                <h4 className="mb-1 text-xs font-semibold uppercase tracking-wide text-secondary">{cat}</h4>
                <div className="grid grid-cols-1 gap-1 md:grid-cols-2">
                  {list.map(p => (
                    <label key={p.key} className="inline-flex items-start gap-1.5 text-sm">
                      <input type="checkbox" disabled={isReadOnly} checked={perms.includes(p.key)} onChange={(e) => setPerms(s => e.target.checked ? [...s, p.key] : s.filter(x => x !== p.key))} />
                      <span>
                        <code className="text-xs">{p.key}</code>
                        {p.dangerous ? <span className="ml-1 text-red-400 text-xs">(dangerous)</span> : null}
                        <div className="text-xs text-secondary">{p.description}</div>
                      </span>
                    </label>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </Field>
        {err ? <p className="text-sm text-red-400">{err}</p> : null}
        <div className="flex justify-end gap-2 pt-2">
          <Btn onClick={onClose}>{isReadOnly ? "Close" : "Cancel"}</Btn>
          {!isReadOnly ? <Btn variant="primary" onClick={() => m.mutate()} disabled={(!role && !name) || m.isPending}>{role ? "Save" : "Create"}</Btn> : null}
        </div>
      </div>
    </Modal>
  );
}

// =========================================================================
// AUDIT
// =========================================================================
export function AuditTab() {
  const [page, setPage] = useState(1);
  const [perPage] = useState(50);
  const [action, setAction] = useState("");
  const [actor, setActor] = useState("");
  const [outcome, setOutcome] = useState("");

  const q = useQuery({
    queryKey: ["admin", "audit", { page, perPage, action, actor, outcome }],
    queryFn: () => auditApi.list({ page, perPage, action: action || undefined, actor: actor || undefined, outcome: outcome || undefined }),
  });

  const totalPages = q.data ? Math.max(1, Math.ceil(q.data.totalCount / perPage)) : 1;

  return (
    <Section title="Audit log" description="Records of authentication, authorization, and administrative actions.">
      <div className="mb-3 flex flex-wrap items-end gap-2">
        <Field label="Action"><input className="input" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} placeholder="e.g. user.create" /></Field>
        <Field label="Actor"><input className="input" value={actor} onChange={(e) => { setActor(e.target.value); setPage(1); }} placeholder="username" /></Field>
        <Field label="Outcome">
          <select className="input" value={outcome} onChange={(e) => { setOutcome(e.target.value); setPage(1); }}>
            <option value="">Any</option>
            <option value="success">success</option>
            <option value="failure">failure</option>
            <option value="denied">denied</option>
          </select>
        </Field>
        <Btn onClick={() => q.refetch()}>Refresh</Btn>
      </div>
      {q.isLoading ? <p className="text-sm text-secondary">Loading…</p> : null}
      {q.data ? (
        <>
          <div className="overflow-x-auto">
            <table className="min-w-full text-sm">
              <thead className="border-b border-app text-left text-xs uppercase tracking-wide text-secondary">
                <tr>
                  <th className="px-2 py-2">When</th>
                  <th className="px-2 py-2">Actor</th>
                  <th className="px-2 py-2">Action</th>
                  <th className="px-2 py-2">Target</th>
                  <th className="px-2 py-2">Outcome</th>
                  <th className="px-2 py-2">IP</th>
                  <th className="px-2 py-2">Detail</th>
                </tr>
              </thead>
              <tbody>
                {q.data.items.map((e) => (
                  <tr key={e.id} className="border-b border-app/40">
                    <td className="px-2 py-2 whitespace-nowrap text-secondary">{new Date(e.occurredAt).toLocaleString()}</td>
                    <td className="px-2 py-2">{e.actorUsername ?? <span className="text-secondary">{e.actorKind}</span>}</td>
                    <td className="px-2 py-2"><code className="text-xs">{e.action}</code></td>
                    <td className="px-2 py-2 text-secondary">{e.targetKind ? `${e.targetKind}:${e.targetId ?? ""}` : "—"}</td>
                    <td className="px-2 py-2">
                      <span className={
                        e.outcome === "success" ? "text-emerald-400" :
                        e.outcome === "denied" ? "text-amber-400" :
                        e.outcome === "failure" ? "text-red-400" : ""}>
                        {e.outcome}
                      </span>
                    </td>
                    <td className="px-2 py-2 text-secondary">{e.ip ?? "—"}</td>
                    <td className="px-2 py-2 text-secondary text-xs max-w-md truncate" title={e.detail ?? undefined}>{e.detail ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="mt-3 flex items-center justify-between text-sm">
            <span className="text-secondary">{q.data.totalCount} total · page {page} of {totalPages}</span>
            <div className="space-x-1">
              <Btn onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}>Prev</Btn>
              <Btn onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page >= totalPages}>Next</Btn>
            </div>
          </div>
        </>
      ) : null}
    </Section>
  );
}

// =========================================================================
// shared
// =========================================================================
function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block text-secondary">{label}</span>
      {children}
    </label>
  );
}

function Modal({ title, onClose, children, wide }: { title: string; onClose: () => void; children: React.ReactNode; wide?: boolean }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div className={`rounded-2xl border border-app bg-surface p-5 shadow-xl ${wide ? "w-full max-w-3xl" : "w-full max-w-lg"}`} onClick={(e) => e.stopPropagation()}>
        <header className="mb-3 flex items-center justify-between">
          <h3 className="text-base font-semibold">{title}</h3>
          <button onClick={onClose} className="text-secondary hover:text-primary">✕</button>
        </header>
        {children}
      </div>
    </div>
  );
}
