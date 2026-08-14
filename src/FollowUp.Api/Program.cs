// Follow-Up Management System — API host.
// The full middleware pipeline (exception→RFC7807, security headers/CSP, correlation id, rate-limit,
// token auth, default-deny + privilege, endpoint + scope), endpoint classes and SPA hosting are built
// in Phase 4 (see docs/BUILD-PLAN.md). This is the minimal bootstrap that composes the layers.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/healthz/live", () => Results.Ok(new { status = "live" }));

app.Run();

// Exposed for integration tests (WebApplicationFactory).
public partial class Program;
