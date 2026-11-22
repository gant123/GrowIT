# Running grow.IT on the home server

grow.IT runs as one Docker container on a home server. It uses that server's existing
services:

- **Database:** the self-hosted Supabase Postgres (`supabase-db` container), in its own
  `growit` database and `growit` role. Only plain Postgres is used — not Supabase auth,
  storage or its API. The app reaches it by container name over the `supabase_default`
  Docker network. Nothing is exposed on the LAN.
- **Public access:** the server's Cloudflare Tunnel (`cloudflared` container on the `proxy`
  network). The tunnel route `growit.now → http://growit-client:8080` sends traffic
  to the app. Cloudflare handles HTTPS.

Why not Cloudflare Pages or Workers: they run static files and JavaScript. This is a .NET
Blazor Server app with a live connection per user, a background report scheduler and
files on disk, so it needs a normal long-running server. Docker on the home server, with
Cloudflare in front through the tunnel, gives that.

Files: [deploy/server/](../deploy/server/) (compose file + `.env.example`) and
[scripts/deploy/](../scripts/deploy/) (`ship-image.sh`, `copy-data.sh`).

In the commands below, `<server>` is your SSH host alias for the server (from
`~/.ssh/config`). Keep the server's address and SSH details there, not in this repo.

## Layout on the server

| What | Where |
| --- | --- |
| Compose project | `~/docker/growit/` (`docker-compose.yml` + `.env`, mode 600) |
| App container | `growit-client`, image `growit-client:<commit>` (also `:latest`) |
| Uploads | volume `growit_uploads` → `/app/wwwroot/uploads` |
| Login keys | volume `growit_keys` → `/app/keys` (`DataProtection__KeysPath`) |
| Database | `growit` database on `supabase-db`, owned by role `growit` |
| Health check | `curl http://127.0.0.1:5180/healthz` on the server |

The server must start Docker (and these containers) on its own after a reboot, or the
site stays down until someone logs in.

## Ship a new version

From a clean checkout of the commit to ship:

```bash
scripts/deploy/ship-image.sh <server>
ssh <server> 'cd ~/docker/growit && docker compose up -d && sleep 5 && curl -fsS http://127.0.0.1:5180/healthz'
```

Database migrations do not run on startup (`Database__AutoMigrate=false`). If the release
adds migrations, either set it to `true` for that deploy, or apply them first:
`dotnet ef database update` against the server's connection string (open a tunnel with
`ssh -L 15432:<supabase-db IP>:5432 <server>`).

**Roll back:** set `GROWIT_IMAGE=growit-client:<previous tag>` in `~/docker/growit/.env`
and run `docker compose up -d`. `ssh <server> docker images growit-client` lists tags.

## Backups

The data lives in Supabase's Postgres. Back it up, drill-test and restore it with the
scripts in `scripts/db/`, as described in
[DB_BACKUP_RESTORE_RUNBOOK.md](DB_BACKUP_RESTORE_RUNBOOK.md):

```bash
DB_SSH_HOST=<server> scripts/db/backup_postgres.sh
```

Back up the `growit_uploads` and `growit_keys` volumes too.

## The original move from the main machine (2026-11)

The app ran from the main development machine as `beta.growit.now`, through that machine's
own tunnel. It moved to the home server and to `growit.now` on 2026-11-22. The steps,
for reference:

1. Ship the image: `scripts/deploy/ship-image.sh <server>`.
2. Create `~/docker/growit/.env` on the server from the running container's settings,
   with `ConnectionStrings__DefaultConnection` pointing at `supabase-db` / `growit`.
3. Rehearse: `DST_DB_PASSWORD=<same password> scripts/deploy/copy-data.sh <server>`, start
   the app, check `/healthz` and a login on the server.
4. Cut over:
   1. On the main machine: `docker stop growit-client` (the site is down from here).
   2. `scripts/deploy/copy-data.sh <server> --replace` (final copy of data, uploads, keys).
   3. On the server: `docker compose up -d` (or `restart` if already running).
   4. In Cloudflare: delete the old DNS records for `growit.now`, then in Zero Trust →
      Networks → Tunnels add a published application route on the home server's tunnel:
      `growit.now` (blank subdomain) → `http://growit-client:8080`. Make sure the
      `growit.now` DNS record is a proxied CNAME to `<tunnel-id>.cfargotunnel.com`.
      Remove the old `beta.growit.now` route from the main machine's tunnel.
   5. Check https://growit.now/healthz and sign in.
5. **Roll back** (before any new data is written on the server): move the route back to
   the main machine's tunnel (`http://0.0.0.0:5180`) and `docker start growit-client` there.

The main machine's `growit-db` container and volumes were left in place as a fallback.
Do not run `copy-data.sh --replace` after the cutover: it would overwrite live data.
