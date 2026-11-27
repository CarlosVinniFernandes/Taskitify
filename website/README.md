# Taskitify website

The Next.js site lives in this directory. English is the default. Brazilian Portuguese is available at `/pt-br`.

## Run locally

Use Node.js 24. Run `npm ci`, copy `.env.example` to `.env.local`, and start with `npm run dev`. Use `npm test` and `npm run build` to verify changes. The pages work with empty configuration. Submitting a valid suggestion requires a private Blob store and a rate-limit secret.

## Deploy on Vercel Hobby

1. Import the GitHub repository into your personal Hobby account. Select **Next.js**, set **Root Directory** to `website`, and select the Git branch you want to deploy.
2. In **Storage**, create a **private** Blob store and connect it to the project. Vercel injects `BLOB_READ_WRITE_TOKEN`. Use a separate store if you want to test suggestions in preview or development.
3. Set `SUGGESTION_RATE_SECRET` for each environment. Generate a stable value with `node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"`.
4. Set `SITE_URL` to the canonical HTTPS origin and `CONTACT_EMAIL` to the confirmed public address. The site offers GitHub Issues for contact while the address is empty. Redeploy after changing these settings. The official Community Soundtrack URL is set in `lib/site.ts`.

The [Hobby plan](https://vercel.com/docs/plans/hobby) is for personal, noncommercial projects. The [Blob allowance](https://vercel.com/docs/vercel-blob/usage-and-pricing) includes 1 GB-month of storage, 10,000 simple operations, 2,000 advanced operations, and 10 GB of transfer per month. Stay on Hobby to use the included quotas without paid overages.

## Moderate suggestions

Open **Storage → your private store → Browse** in Vercel. Look under `suggestions/YYYY-MM/` for independent JSON objects with UUID, song, artist, optional name, submission date, and pending status. Review the files there and add selected tracks manually in Spotify. There's no public list or read route.

The API accepts one suggestion per address per minute using an atomic Blob write. The path uses a rotating keyed hash. The JSON stores no IP address. For future app integration, `/community?song=Song&artist=Artist&name=Nickname#suggest` pre-fills the editable form. Sending still requires an explicit submit.

Product images come from the [Store campaign and onboarding assets](../marketing/microsoft-store/SOURCES.md). Font license notices are in `public/fonts/`.

## Send a Song experiment

Run `npm run dev` and open `http://localhost:3000/s#CODE` with a code from `shared/lyrics-share-v1.md` on the desktop branch `t3/lyrics-share`. If testing a full desktop link locally, keep its fragment and replace the host. For a non-production build, set `LYRICS_SHARE_EXPERIMENT=1` before building. Keep previews restricted to testers while lyric-sharing rights are being verified. The viewer is disabled when `VERCEL_ENV=production` and is excluded from navigation and search indexing.

Visit `/s/demo` in development to try Letter, CassetteTape and Retro in direct and surprise modes with original demonstration lyrics.

To test the short-link API, enable `LYRICS_SHORT_LINK_EXPERIMENT=1`, connect a private Blob store to the test environment, and set `SHARE_CODE_RATE_SECRET` to a server-only secret of at least 32 characters. On hosted previews, set `SITE_URL` to the preview origin and enable `LYRICS_SHARE_EXPERIMENT=1` as well. The viewer and API stay disabled in Vercel production even with all flags set. See the [short-link API, retention and Hobby quota instructions](../shared/lyrics-short-link-v1.md).
