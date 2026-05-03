Setup instructions
Clone the repo. From the project root run dotnet restore and dotnet run, then open the URL printed in the terminal. Run dotnet test for automated tests. Optional: install Tesseract and add it to PATH for image OCR; without it, images still ingest and can be fixed in the review UI. To run the same stack as production locally: docker build -t mastery-task . and docker run -p 8080:8080 mastery-task, then open http://localhost:8080. For a public demo, deploy with Docker (e.g. Render): use the repo Dockerfile, set ASPNETCORE_ENVIRONMENT=Production, and let the host set PORT (the app listens on that port in the container).


Approach
We built an end-to-end pipeline: ingest from the bundled resources/ folder or manual uploads (PDF, TXT, CSV, common image types), extract a consistent document model (type, supplier, number, dates, currency, line items, money fields) using heuristics plus PdfPig for PDFs and optional Tesseract for images, then validate with explicit rules (missing fields, dates, line/total math, duplicate document numbers). Documents with problems go to Needs Review; the UI supports manual corrections and re-validation, Validated only when there are zero issues, Rejected otherwise. SQLite stores state; the dashboard summarizes volumes, issues, statuses, and totals by currency.


AI tools used
AI-assisted tooling (e.g. Cursor / LLM) helped with scaffolding and early parser/regex drafts. Final behaviour, validation rules, persistence, UI, tests, and deploy configuration were reviewed and adjusted manually to match the task.


Improvements with more time
Stronger OCR (cloud APIs, confidence scores, layout-aware extraction), broader tests and CI, more reliable extraction per template/vendor, and production-oriented hardening (logging, monitoring, durable storage if scaled beyond a single instance)
