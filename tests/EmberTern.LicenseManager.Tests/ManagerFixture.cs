using System;
using System.IO;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// A whole License Manager in a temporary folder: its own keystore, its own register, its own key.
///
/// <para>⭐ It performs a REAL ceremony rather than stubbing one. The point of an end-to-end test here is
/// that the ceremony, the keystore, the issuer and the client verifier agree — and every one of those
/// links is exactly where a stub would hide a defect.</para>
/// </summary>
internal sealed class ManagerFixture : IDisposable
{
    internal const string Passphrase = "six generated words kept on paper too";

    private readonly string _root;

    internal ManagerFixture(DateTimeOffset? now = null)
    {
        Now = now ?? new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        _root = Path.Combine(Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N"));

        Paths = new ManagerPaths(_root);
        Paths.EnsureFolder();

        Register = LicenseRegister.Open(Paths.Register, () => Now, actor: "tester");
        Session = SigningSession.Create(Paths, "R1", Passphrase, Now);
        Workflow = new IssuingWorkflow(Register, () => Now);
    }

    internal DateTimeOffset Now { get; set; }

    internal ManagerPaths Paths { get; }

    internal LicenseRegister Register { get; }

    internal SigningSession Session { get; }

    internal IssuingWorkflow Workflow { get; }

    internal CustomerRecord SaveCustomer(string name = "ACME Sp. z o.o.") =>
        Register.SaveCustomer(new CustomerRecord { CustomerId = Register.NextCustomerId(), Name = name });

    internal LicenseRecord SaveLicense(CustomerRecord customer, int seats = 5, int years = 1) =>
        Register.SaveLicense(new LicenseRecord
        {
            LicenseId = EmberTern.Licensing.Issuing.LicenseIssuer.NewLicenseId(),
            CustomerId = customer.CustomerId,
            Product = EmberTern.Licensing.LicenseConstants.ProductId,
            Seats = seats,
            NotBefore = Now,
            ExpiresAt = Now.AddYears(years),
            Status = LicenseStatuses.Active,
        });

    public void Dispose()
    {
        Session.Dispose();
        Register.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}
