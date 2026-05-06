---
name: Multer has no file size or MIME type limits
description: All six file upload routes use multer({ storage: memoryStorage() }) with no limits or fileFilter — any file type and any size accepted into server RAM
type: project
---

server/src/routes/import.ts line 9: `const upload = multer({ storage: multer.memoryStorage() })` — no limits object, no fileFilter.

**Why:** Six upload routes (amex, barclays, santander, hsbc, chase, sofi) share this instance.

**How to apply:** When adding new upload routes, always require limits: { fileSize: X } and fileFilter validating mimetype. Recommend 10MB ceiling for PDFs, application/pdf MIME filter.
