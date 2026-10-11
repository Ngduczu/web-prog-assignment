# Deploy LMS to a VPS with GitHub Actions

This repository and `frontend/` are separate GitHub repositories. Each has its own CI workflow; both deploy the matching image to one Docker Compose stack on the VPS.

## Deployment shape

- GitHub Actions runs checks on pull requests and pushes to `main`.
- A successful push to `main` builds an immutable image tagged with its commit SHA and pushes it to GHCR.
- The backend workflow uploads `docker-compose.prod.yml`, `nginx.conf.template`, and `deploy.sh` to the VPS, then deploys the API.
- The frontend workflow deploys the frontend image. Deployments from both repositories are serialized on the VPS.
- nginx (certbot-issued certificates) routes `FRONTEND_DOMAIN` to Next.js and `API_DOMAIN` to ASP.NET Core.
- PostgreSQL, Redis, and uploaded files use Docker volumes and are not published to the public network.

> Publish the backend once before the frontend the first time. Confirm the domain, secrets, GHCR pull login, and `/opt/lms/.env` are ready first.

## 1. Check the VPS

These instructions assume an Ubuntu/Debian x86-64 VPS with a public static IP and a clean port 80/443. Check architecture using `uname -m`; the supplied workflows build `linux/amd64` images, so an `aarch64` VPS needs multi-platform build configuration before deployment.

1. Install a supported Docker Engine and Docker Compose plugin from Docker's official repository.
2. Create a non-root deployment account, for example `lms-deploy`, and give it Docker access. Membership in the `docker` group is effectively root access; protect its SSH key accordingly.
3. Configure the firewall/security group to allow inbound SSH (preferably restricted to trusted source IPs), TCP 80, and TCP 443. UDP 443 is optional for HTTP/3. Do not open 3000, 8080, 5432, or 6379.
4. Make sure a different service is not already using ports 80/443.
5. Create `/opt/lms` owned by the deployment account with permissions `700`.

The API currently applies database migrations at startup. Keep one API replica for this deployment and take a database backup before releases that change the schema.

## 2. Configure DNS and HTTPS domains

Create DNS `A` records pointing to the VPS IP for both names, for example:

- Frontend: `sfit-study.ducvu.site`
- Backend/SignalR: `study.ducvu.site`

If using IPv6, only add `AAAA` records when the VPS IPv6 route and firewall are working. Certbot needs public DNS resolution and inbound HTTP/HTTPS reachability to obtain certificates. Do not put a raw VPS IP in OAuth callback URLs; use these HTTPS domain names.

## 3. Prepare an SSH deployment key

On a trusted administrator computer, generate a dedicated key pair for GitHub Actions. Do not send the private key in chat or commit it. Add only the public key to the deployment user's `~/.ssh/authorized_keys` on the VPS; protect that file and the private key.

Obtain the VPS SSH host key and verify its fingerprint against the VPS through a trusted channel before saving the verified `known_hosts` line. The workflows require strict host-key checking and will not accept unknown hosts automatically.

For stronger isolation, use a dedicated deploy user/key and restrict its SSH access as appropriate for your operations. The user needs Docker access to deploy containers.

## 4. Create the production environment file on the VPS

Create `/opt/lms/.env` directly on the VPS, owned by the deployment user with mode `600`. Never commit or upload this file. The example below uses placeholders: replace them locally on the VPS, do not paste actual secrets into GitHub workflow YAML or this document.

```dotenv
COMPOSE_PROJECT_NAME=lms
FRONTEND_DOMAIN=sfit-study.ducvu.site
API_DOMAIN=study.ducvu.site

BACKEND_IMAGE=ghcr.io/ngduczu/web-prog-assignment
BACKEND_IMAGE_TAG=main
FRONTEND_IMAGE=ghcr.io/ngduczu/web-pro-assignment-fe
FRONTEND_IMAGE_TAG=main

POSTGRES_DB=lms
POSTGRES_USER=lms_app
POSTGRES_PASSWORD=REPLACE_WITH_RANDOM_HEX_SECRET
REDIS_PASSWORD=REPLACE_WITH_ANOTHER_RANDOM_HEX_SECRET
JWT_SIGNING_KEY=REPLACE_WITH_AT_LEAST_32_RANDOM_BYTES
JWT_ISSUER=LMS.Api
JWT_AUDIENCE=LMS.Client
JWT_EXPIRATION_MINUTES=60

# Fill in when email features are enabled. Prefer an SMTP app password.
MAIL_HOST=smtp.gmail.com
MAIL_PORT=587
MAIL_ENABLE_SSL=true
MAIL_USERNAME=
MAIL_PASSWORD=
MAIL_FROM_EMAIL=
MAIL_FROM_NAME=LMS

# Optional initial system account. Review the seeder behavior before setting these.
SYSTEMACCOUNT_EMAIL=
SYSTEMACCOUNT_PASSWORD=

# Optional Google OAuth. Register the exact backend callback as an authorized URI.
OAUTH_GOOGLE_CLIENT_ID=
OAUTH_GOOGLE_CLIENT_SECRET=
OAUTH_GOOGLE_REDIRECT_URI=https://study.ducvu.site/api/auth/oauth/Google/callback
OAUTH_GOOGLE_CLIENT_CALLBACK_URI=https://sfit-study.ducvu.site/oauth/callback

# Leave disabled for local volume storage. Configure S3 values before enabling it.
S3_STORAGE_ENABLED=false
S3_STORAGE_BUCKET_NAME=
S3_STORAGE_REGION=us-east-1
S3_STORAGE_SERVICE_URL=
S3_STORAGE_ACCESS_KEY=
S3_STORAGE_SECRET_KEY=
S3_STORAGE_FORCE_PATH_STYLE=false
```

Generate independent random database, Redis, and JWT values on the VPS (for example with `openssl rand -hex 32`). Avoid whitespace and Compose interpolation characters in database/Redis passwords; hexadecimal values are convenient. Keep SMTP/OAuth/S3 credentials private. If an SMTP password contains special characters or spaces, quote/escape it according to Docker Compose `.env` syntax, then verify the resolved configuration without printing secrets.

`POSTGRES_PASSWORD` and `REDIS_PASSWORD` are first-use container credentials. Changing them later in `.env` does not change credentials already initialized in the persistent volumes; plan credential rotation explicitly.

The application seeds/promotes an account to the System role when both `SYSTEMACCOUNT_EMAIL` and `SYSTEMACCOUNT_PASSWORD` are set. Inspect that behavior and use a controlled account before enabling these values.

## 5. Allow VPS image pulls from GHCR

The images are published to these packages:

- `ghcr.io/ngduczu/web-prog-assignment`
- `ghcr.io/ngduczu/web-pro-assignment-fe`

Make the packages private unless public distribution is intended. On the VPS, sign in to `ghcr.io` using a GitHub token with package **read-only** access. Enter the token through `docker login --password-stdin`; do not put it in a shell command argument, `.env`, repository, or terminal transcript. Confirm the login is stored for the deployment user that runs Docker Compose. GitHub Actions uses its per-run `GITHUB_TOKEN` to publish; the VPS pull credential is separate.

In each GitHub repository, confirm Actions is enabled and the workflow token is allowed to publish packages. If GHCR reports that the package is not linked/authorized for the repository, grant that repository access to the package in the package's GitHub settings.

## 6. Add GitHub Actions secrets to both repositories

Add the same repository Actions secrets in **both** GitHub repositories:

- `DEPLOY_HOST`: VPS DNS name or public IP.
- `DEPLOY_PORT`: SSH port, commonly `22`.
- `DEPLOY_USER`: dedicated deployment account.
- `DEPLOY_SSH_KEY`: private key corresponding to the public key installed for that user.
- `DEPLOY_KNOWN_HOSTS`: verified SSH host-key line(s) for the host/port.
- `DEPLOY_PATH`: `/opt/lms` (use a path without spaces).

The backend workflow also deploys the Compose/nginx files. The frontend deployment expects the backend workflow to have installed `/opt/lms/deploy.sh` first.

Protect `main` with pull requests and required checks. Restrict who can modify workflows and who can approve production deployments. Never expose deployment secrets to fork pull requests; the supplied workflows do not publish or deploy on pull requests.

## 7. First deployment (backend, then frontend)

1. Ensure `/opt/lms/.env` exists and is mode `600`, all mandatory values are filled, both DNS records resolve to the VPS, and the VPS can pull private GHCR packages.
2. In the backend repository, review and merge the deployment files to `main`. The backend workflow runs restore, Release build, and the test suite; only after success does it publish and deploy the API.
3. Check the backend Actions run. On the VPS, inspect `docker compose --env-file /opt/lms/.env -f /opt/lms/docker-compose.prod.yml ps` and logs for API/database/Redis startup or migration errors.
4. In the frontend repository, review and merge its Dockerfile/workflow to `main`. Its workflow runs `npm ci`, lint, TypeScript, and production build before image publication/deployment.
5. Confirm nginx has started with valid certificates. Visit the frontend over HTTPS and exercise sign-in, API requests, uploads, and SignalR.

Subsequent pushes to either repository's `main` run that repository's checks and deploy only its image. A failed check prevents publishing/deployment. Each image also keeps the short-lived/current `main` tag and an immutable full commit-SHA tag.

## 8. OAuth and public-service settings

With the example domains above, set these environment values on the VPS:

- `OAUTH_GOOGLE_REDIRECT_URI=https://study.ducvu.site/api/auth/oauth/Google/callback`
- `OAUTH_GOOGLE_CLIENT_CALLBACK_URI=https://sfit-study.ducvu.site/oauth/callback`

Register the first URL exactly in Google Cloud Console under **Authorized redirect URIs**. `CORS__AllowedOrigins` is configured by Compose as `https://sfit-study.ducvu.site`. The browser SignalR URL is `https://study.ducvu.site/hubs/chat`; the Next.js server calls the API internally at `http://api:8080/api`.

Do not enable OAuth until the domain, TLS, Google client credentials, and both callback URLs have been verified.

## 9. Verify, operate, and recover

- Check container status and logs without dumping `.env` or secrets. Confirm the API completed database migration and the frontend is healthy through real user journeys.
- Verify HTTPS redirects/certificates and the SignalR WebSocket connection from a real browser.
- Set up off-VPS encrypted PostgreSQL backups and test restoring one. Docker volumes are persistence, not backups. Back up uploads too, or move them to S3/object storage before depending on them.
- Monitor disk space, container restarts/logs, backup success, certificate renewal, and application errors.
- The API's existing rate limiter uses the direct peer IP, while this setup places nginx in front of it. Before production traffic, configure ASP.NET Core to trust forwarded headers only from the nginx proxy/network and ensure limits use the actual client IP. The repository approval policy requires explicit approval before changing this backend behavior.
- For a rollback, update only the relevant `*_IMAGE_TAG` in `/opt/lms/.env` to a previously published 40-character commit SHA, then run `docker compose --env-file /opt/lms/.env -f /opt/lms/docker-compose.prod.yml pull api` (or `frontend`) followed by `docker compose --env-file /opt/lms/.env -f /opt/lms/docker-compose.prod.yml up -d api` (or `frontend nginx`). Be aware an older API image may not be compatible with a newer database schema; restore/rollback planning must account for migrations.

## Files introduced for deployment

- Backend: `Dockerfile`, `.dockerignore`, `docker-compose.prod.yml`, `nginx.conf.template`, `deploy.sh`, `.github/workflows/ci-deploy.yml`.
- Frontend: `Dockerfile`, `.dockerignore`, `.github/workflows/ci-deploy.yml`.
