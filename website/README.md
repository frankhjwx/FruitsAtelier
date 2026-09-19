# FruitsAtelier website

Public URL: https://fruitsatelier.himiko.moe/

`public/` is the complete static website. It contains English HTML, CSS,
JavaScript, logos, and the approved project screenshots. No server-side runtime
or build step is required. Downloads link to GitHub Releases.

## Hosting

The site shares the existing Debian/Nginx server with the personal homepage.
Its independent document root is `/var/www/fruitsatelier/current`, a symlink
to a timestamped directory under `/var/www/fruitsatelier/releases/`.
The personal homepage uses `/var/www/himiko/current`.

`deployment/nginx.conf` is the FruitsAtelier virtual host. Cloudflare proxies
the subdomain; the origin has a dedicated Let's Encrypt certificate. ACME
validation uses `/var/www/letsencrypt`. The existing Certbot timer renews it;
`deployment/renewal-hook.sh` reloads Nginx after this certificate renews.

## Publishing updates

1. Edit and preview `public/` locally.
2. Package only `public/` contents, with a separate SHA-256 manifest.
3. Upload using the existing SSH deployment identity and `deploy` account.
4. Extract into a new release directory and verify every file against the manifest.
5. Atomically replace `current` with a symlink to the verified release.
6. Verify the public HTTPS page, assets, screenshot controls, and personal homepage.
7. Retain the previous release for rollback. Purge affected Cloudflare assets
   when replacing files at unchanged URLs if immediate cache invalidation is needed.

Nginx serves CSS, JavaScript, and images with a one-hour cache lifetime.
HTML requires cache revalidation. Legacy `/zh` and `/en` entry paths redirect
to `/?site=fruitsatelier` to recover browsers with a cached personal-homepage
language selector from before this virtual host was enabled.
Configuration changes require `nginx -t` followed by a reload. Test renewal
with `certbot renew --cert-name fruitsatelier.himiko.moe --dry-run --run-deploy-hooks --no-random-sleep-on-renew`.
Do not upload source tooling, deployment configuration, capture projects, or logs.

## Release downloads

The Windows download buttons use GitHub's `/releases/latest/download/FruitsAtelier-win-x64.zip`
URL. The Windows release workflow uploads this fixed-name copy and its SHA256
alongside the versioned ZIP before publishing each release. The alias has exactly
the same bytes as the versioned package. Future stable releases update the download
automatically without deploying the website or giving CI access to the web server.
Prereleases do not replace GitHub's latest stable release.

`public/release.js` optionally fetches the latest public release metadata to show
its version and ZIP size. Failed or rate-limited API requests leave neutral labels
and the working static download links intact. Release notes also point at the
latest release; the manual links to the maintained source document.
