# Server & Hosting Requirements — DR Bahig Books Portal

## Architecture Overview

The system consists of two parts that require hosting:

| Component | Type | Hosting |
|---|---|---|
| **Web Portal** | Blazor Server (.NET 10) + Web API | Production server (VPS or ASP.NET shared) |
| **Database** | SQL Server | Same or separate server |
| **Print Agent** | Local Windows app | Runs on each shop's PC (no server needed) |

---

## Web Portal Server Requirements

### Minimum Specs (10–20 concurrent shops, ~50–100 users)

| Resource | Requirement | Reason |
|---|---|---|
| CPU | 2 vCPU (2.5 GHz+) | PDF encryption/decryption with iText7, watermarking with PdfSharpCore |
| RAM | 4 GB (8 GB recommended) | Blazor Server holds a SignalR connection per client (~50–100 MB per client); PDF processing is memory-heavy |
| Storage | 50 GB SSD | PDF file storage (scales with usage) |
| OS | Windows Server 2019+ or Linux | .NET 10 runs on both |
| Bandwidth | 100 Mbps (unmetered) | PDF uploads/downloads for print jobs |

### Recommended Specs (50+ shops, 200+ users)

| Resource | Requirement |
|---|---|
| CPU | 4 vCPU |
| RAM | 8–16 GB |
| Storage | 100+ GB SSD (expandable) |
| OS | Windows Server 2022 or Ubuntu 24.04 LTS |
| Bandwidth | 1 Gbps |

### Software Stack

| Component | Version / Spec |
|---|---|
| .NET Runtime | .NET 10 (ASP.NET Core Runtime) |
| Web Server | IIS (Windows) or Nginx/Kestrel (Linux) |
| Database | SQL Server 2019+ (Express is OK for <20 shops) |
| SignalR Transport | WebSockets required (must be enabled on hosting) |

---

## Database Server

| Resource | 10–20 Shops | 50+ Shops |
|---|---|---|
| CPU | 1 vCPU | 2 vCPU |
| RAM | 2 GB | 4 GB |
| Storage | 20 GB | 50 GB |
| Edition | SQL Server Express (10 GB limit) or Standard | SQL Server Standard+ |

If using a single server for both app + DB, add the DB requirements to the web server specs.

---

## Hosting Options Comparison

### Option 1: ASP.NET Shared Hosting (current — RunASP.NET)

| Aspect | Rating | Notes |
|---|---|---|
| Cost | Low (~$5–15/mo) | RunASP.NET, SmarterASP, etc. |
| Ease of setup | High | Pre-installed .NET, IIS, SQL Server |
| Performance | Low–Medium | Shared CPU/RAM, noisy neighbors |
| Scalability | Low | Cannot scale beyond plan limits |
| Blazor Server + SignalR | Medium | Must confirm WebSocket support |
| PDF storage limits | Low | Shared disk space is tight |
| **Verdict** | Good for start / pilot | Will hit limits as users grow |

### Option 2: VPS (Linux) + SQL Server (separate)

| Aspect | Rating | Notes |
|---|---|---|
| Cost | Medium (~$20–50/mo total) | Hetzner, DigitalOcean, Vultr |
| Performance | High | Dedicated vCPU, full RAM |
| Control | Full | Root access, custom config |
| SQL Server licensing | Extra | SQL Server Standard license needed |
| **Verdict** | Best value for growing system | Can use SQL Server Express free tier (10 GB) |

### Option 3: VPS (Windows)

| Aspect | Rating | Notes |
|---|---|---|
| Cost | Higher (~$30–80/mo) | Windows license adds $15–30/mo |
| Setup | Easy | IIS + SQL Server on same box |
| Performance | High | Dedicated resources |
| **Verdict** | Convenient if team knows Windows | More expensive than Linux VPS |

### Option 4: Cloud (Azure / AWS)

| Aspect | Rating | Notes |
|---|---|---|
| Cost | High (~$50–200+/mo) | Pay-as-you-go, managed services |
| Scalability | Unlimited | Auto-scale, load balancers |
| Managed DB | Azure SQL / RDS | No manual DB admin |
| Managed storage | Blob storage for PDFs | Scalable, cheap |
| **Verdict** | Best for production at scale | Overkill / too expensive for start |

---

## Recommended Setup (Growth Path)

### Phase 1 — Pilot (current, 5–10 shops)
- **Hosting:** RunASP.NET (current) or similar shared ASP.NET host
- **Database:** Included SQL Server on the same plan
- **Cost:** ~$10–20/mo

### Phase 2 — Growth (20–50 shops)
- **App Server:** Linux VPS (Hetzner CX22 or CX32) — €8–15/mo
  - 2–4 vCPU, 4–8 GB RAM, 80 GB SSD
  - Ubuntu 24.04, Nginx reverse proxy, Kestrel
- **Database:** SQL Server Express on Windows VPS — $15/mo
  - Or Azure SQL Database Serverless (~$10–20/mo)
- **Total:** ~$25–40/mo

### Phase 3 — Scale (50+ shops)
- **App Server:** 2× Linux VPS with load balancer
- **Database:** Azure SQL or SQL Server Standard on dedicated VPS
- **PDF Storage:** Azure Blob / S3-compatible storage
- **Total:** ~$100–200/mo

---

## Critical Checklist for Any Hosting Provider

- [ ] Supports ASP.NET Core / .NET 10 runtime
- [ ] WebSockets enabled (required for Blazor Server / SignalR)
- [ ] SQL Server hosting available (or ability to connect to external SQL Server)
- [ ] Enough storage for PDF files (estimate: ~2–5 MB per PDF × number of books)
- [ ] HTTPS with custom domain support
- [ ] Adequate bandwidth (PDF downloads can be 5+ MB per print job)
- [ ] Backup / snapshot capability for database and files
- [ ] Forwarded Headers support if behind a reverse proxy

---

## PDF Storage Estimation

| Scale | Number of Books | Avg PDF Size | Total Storage |
|---|---|---|---|
| Small | 50 | 3 MB | 150 MB |
| Medium | 200 | 5 MB | 1 GB |
| Large | 1,000 | 8 MB | 8 GB |
| Secure prints (temp) | Up to 50 jobs | 5 MB | 250 MB (TTL 5 min) |

Plan for 5× growth buffer on storage.

---

## Deployment Checklist

1. Install .NET 10 Runtime on server
2. Publish app (Release config, single-file or not)
3. Copy published files to server
4. Set connection string in `appsettings.Production.json` (or environment variables)
5. Set up database (run migrations on startup or manual)
6. Configure HTTPS certificate
7. Enable WebSockets in IIS / Nginx
8. Test SignalR connection
9. Test PDF upload and print flow
10. Set up monitoring and backups
