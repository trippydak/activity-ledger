# Activity Ledger Cloud

Owner-private read-only dashboard and upload-only snapshot endpoint hosted through Sites. Storage uses the managed BUCKET binding. Browser identity is supplied by Sites; every viewer API requires that authenticated identity and scopes objects to its hash. Device upload keys cannot call viewer APIs. Service-access credentials alone carry no viewer identity.

Build: `node scripts/build.mjs`. Test: `node --test tests/cloud.test.mjs`. Production runtime secret: `SITES_SERVICE_TOKEN` (configure through Sites; never commit its value). No dependencies or install step is needed.

The build emits one self-contained Worker at `dist/server/index.js`. Publish archives contain `.openai/hosting.json` and that Worker under its `dist/server` path. Keep the Site owner-private. Source credentials must be supplied via stdin/session memory and never committed.

The pairing download intentionally contains an upload key and the Site's service-access credential for a specific Windows installation. It is shown only during authenticated owner pairing; Windows stores it encrypted with DPAPI. Device endpoints require the separate hashed device key and cannot read histories or impersonate the signed-in browser. Production checks confirmed forged viewer headers are stripped at the platform boundary.

Cloud snapshots are separately stored user data. Disabling local sync or revoking a key does not erase the last snapshot. Empty snapshots after an enabled local history deletion replace the current view. Incomplete uploads can leave unreferenced storage objects; this is not an archival or end-to-end encrypted backup service. Remote viewing does not add any recording management API.
