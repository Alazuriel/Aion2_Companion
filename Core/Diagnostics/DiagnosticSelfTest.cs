// Windows: AION 2 Companion.exe --diagnostics-self-test
// Pure local test: never sends HTTP requests or real diagnostic data.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AION_2_Companion.Core.Diagnostics;

public static class DiagnosticSelfTest
{
    public static int Run()
    {
        const string canary = "PRIVATE_EMAIL_foo@example.com_SECRET_TOKEN_123456";
        try
        {
            var ex = new InvalidOperationException(canary);
            var report = DiagnosticSchema.Build("managed_crash", "confirmed", "phaseward", null, DiagnosticSchema.Categorize(ex));
            string localJson = DiagnosticSchema.SerializeForReviewAndUpload(report);
            string apiJson = DiagnosticApiV42.Serialize(report);
            if (localJson.Contains(canary, StringComparison.Ordinal) || apiJson.Contains(canary, StringComparison.Ordinal) ||
                apiJson.Contains("example.com", StringComparison.Ordinal))
                throw new Exception("privacy boundary failed");
            if (!DiagnosticApiV42.IsValidPinnedBody(apiJson, report.ReportId))
                throw new Exception("V42 API schema gate failed");
            using (var jsonDoc = JsonDocument.Parse(apiJson))
            {
                var root = jsonDoc.RootElement;
                if (root.GetProperty("schema_version").GetInt32() != 1 ||
                    root.GetProperty("event").GetProperty("kind").GetString() != "managed_exception" ||
                    root.GetProperty("event").GetProperty("confidence").GetString() != "confirmed" ||
                    root.TryGetProperty("report_id", out _))
                    throw new Exception("V42 field mapping failed");
            }
            // Local-only test. A recent Phaseward marker provides context,
            // not a confirmed root cause and never a new freeform API field.
            var phasewardTime = DateTimeOffset.UtcNow;
            var phasewardHeartbeat = new DiagnosticHeartbeat
            {
                SessionId = Guid.NewGuid().ToString("N"),
                LastUiUtc = phasewardTime.AddSeconds(-24),
                LastWorkerUtc = phasewardTime,
                Breadcrumbs = new List<DiagnosticBreadcrumb>
                {
                    new() { Code = "phaseward_boss_seen", Utc = phasewardTime.AddSeconds(-12) },
                    new() { Code = "phaseward_event", Utc = phasewardTime.AddSeconds(-8) }
                }
            };
            var phasewardHang = DiagnosticSchema.Build("suspected_ui_hang", "suspected", "ui",
                phasewardHeartbeat, now: phasewardTime);
            string phasewardApiJson = DiagnosticApiV42.Serialize(phasewardHang);
            if (!DiagnosticApiV42.IsValidPinnedBody(phasewardApiJson, phasewardHang.ReportId))
                throw new Exception("Phaseward V42 schema mismatch");
            using (var phasewardDoc = JsonDocument.Parse(phasewardApiJson))
            {
                var ev = phasewardDoc.RootElement.GetProperty("event");
                if (ev.GetProperty("kind").GetString() != "ui_hang_suspected" ||
                    ev.GetProperty("confidence").GetString() != "suspected" ||
                    ev.GetProperty("subsystem").GetString() != "integration")
                    throw new Exception("Phaseward contextual classification failed");
                if (phasewardApiJson.Contains("phaseward_", StringComparison.Ordinal) ||
                    phasewardApiJson.Contains(canary, StringComparison.Ordinal))
                    throw new Exception("Phaseward context leaked into V42 JSON");
            }
            // An old observation must not misattribute a later unrelated hang.
            phasewardHeartbeat.Breadcrumbs[0].Utc = phasewardTime.AddSeconds(-90);
            phasewardHeartbeat.Breadcrumbs[1].Utc = phasewardTime.AddSeconds(-90);
            var laterHang = DiagnosticSchema.Build("suspected_ui_hang", "suspected", "ui",
                phasewardHeartbeat, now: phasewardTime);
            using (var laterDoc = JsonDocument.Parse(DiagnosticApiV42.Serialize(laterHang)))
                if (laterDoc.RootElement.GetProperty("event").GetProperty("subsystem").GetString() != "ui")
                    throw new Exception("Old Phaseward activity misclassified UI hang");
            var missing = DiagnosticSchema.Build("unexpected_termination", "uncertain", "unknown", null);
            if (missing.Confidence != "uncertain" || missing.ExceptionCategory != null) throw new Exception("classification failed");
            string uncertain = DiagnosticApiV42.Serialize(missing);
            if (!uncertain.Contains("\"unexpected_exit\"", StringComparison.Ordinal) ||
                !uncertain.Contains("\"unknown\"", StringComparison.Ordinal))
                throw new Exception("unexpected-exit confidence failed");
            var fake = DiagnosticSchema.Build("manual_report", "manual", "unknown", null);
            fake.Breadcrumbs.Add(new DiagnosticBreadcrumb { Code = canary, Utc = DateTimeOffset.UtcNow });
            if (DiagnosticSchema.IsSafe(fake)) throw new Exception("event allowlist failed");
            if (DiagnosticApiV42.IsValidPinnedBody(apiJson.Replace("\"client\"", "\"secret\"", StringComparison.Ordinal), report.ReportId))
                throw new Exception("unknown field accepted");
            if (OperatingSystem.IsWindows())
            {
                DiagnosticStore.Save(report);
                var read = DiagnosticStore.Read(report.ReportId);
                if (read == null || read.IncidentKind != "managed_crash") throw new Exception("encrypted roundtrip failed");
                var pinned1 = DiagnosticStore.PinUpload(report.ReportId);
                DiagnosticStore.MarkAuthorizedPending(report.ReportId);
                if (!DiagnosticStore.HasAuthorizedPending(report.ReportId)) throw new Exception("pending flag failed");
                DiagnosticStore.SetRetryAfter(report.ReportId, 120);
                if (DiagnosticStore.GetRetryAfterSeconds(report.ReportId) < 110) throw new Exception("rate limit persistence failed");
                DiagnosticStore.CancelAuthorizedPending(report.ReportId);
                if (DiagnosticStore.HasAuthorizedPending(report.ReportId)) throw new Exception("pending cancel failed");
                if (pinned1 != DiagnosticStore.PreviewUpload(report.ReportId)) throw new Exception("pinned body mismatch");
                DiagnosticStore.Delete(report.ReportId);
                if (DiagnosticStore.Read(report.ReportId) != null) throw new Exception("delete failed");

                var recovered = DiagnosticSchema.Build("suspected_ui_hang", "suspected", "ui", null);
                DiagnosticStore.Save(recovered);
                string first = DiagnosticStore.PinUpload(recovered.ReportId);
                DiagnosticStore.MarkRecovered(recovered.ReportId);
                string second = DiagnosticStore.PinUpload(recovered.ReportId);
                if (first != second) throw new Exception("idempotent payload changed after recovery");
                DiagnosticStore.Delete(recovered.ReportId);
            }
            Console.WriteLine("PASS: V42 allowlist, no secrets, UUIDv4, encryption, pinned retry, delete, recovery");
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine("FAIL: diagnostic self-test (details omitted)");
            return 1;
        }
    }
}
