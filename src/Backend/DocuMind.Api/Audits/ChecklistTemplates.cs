using DocuMind.Api.Entities;

namespace DocuMind.Api.Audits;

public record ChecklistTemplate(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<(string Title, string Requirement, AuditSeverity Severity)> Checks);

// Starter checklists a company can copy and then adapt. They are generic starting points,
// not legal advice; companies should tune requirements (e.g. notice periods) to their own policies.
public static class ChecklistTemplates
{
    public static readonly IReadOnlyList<ChecklistTemplate> All =
    [
        new(
            "commercial-contract",
            "Commercial contract essentials",
            "Core commercial terms to check in supplier and customer agreements.",
            [
                ("Term and termination", "The contract states how long it lasts and how either party can terminate it, including any notice period.", AuditSeverity.Medium),
                ("Termination notice of at least 60 days", "Termination for convenience requires at least 60 days' written notice.", AuditSeverity.Medium),
                ("Automatic renewal", "If the contract renews automatically, it states the renewal term and the deadline for giving notice not to renew.", AuditSeverity.Medium),
                ("Limitation of liability", "Each party's total liability is capped (for example at the fees paid in the previous 12 months), and any exclusions from the cap are listed.", AuditSeverity.High),
                ("Indemnification", "The supplier indemnifies the customer against third-party claims, including claims that the supplier's products or services infringe intellectual property rights.", AuditSeverity.High),
                ("Payment terms", "Payment terms are defined, with invoices payable no sooner than 30 days after receipt.", AuditSeverity.Medium),
                ("Confidentiality", "Both parties must keep the other's confidential information confidential, and this obligation continues after the contract ends.", AuditSeverity.High),
                ("Governing law and disputes", "The contract names the governing law and the courts or arbitration process for disputes.", AuditSeverity.Low),
            ]),
        new(
            "gdpr-processor",
            "Data processing agreement (GDPR Article 28)",
            "Terms a controller-processor agreement must contain under GDPR Article 28(3).",
            [
                ("Scope of processing", "The agreement describes the subject matter, duration, nature and purpose of the processing, the types of personal data and the categories of data subjects.", AuditSeverity.Medium),
                ("Documented instructions", "The processor may process personal data only on the controller's documented instructions.", AuditSeverity.High),
                ("Staff confidentiality", "People authorised to process the personal data are bound by confidentiality.", AuditSeverity.Medium),
                ("Security measures", "The processor must implement appropriate technical and organisational security measures (GDPR Article 32).", AuditSeverity.High),
                ("Sub-processors", "Sub-processors may be engaged only with the controller's prior authorisation and under the same data protection obligations.", AuditSeverity.High),
                ("Data subject requests", "The processor must help the controller respond to data subjects exercising their rights.", AuditSeverity.Medium),
                ("Breach notification", "The processor must notify the controller of a personal data breach without undue delay, preferably within a stated timeframe such as 48 hours.", AuditSeverity.High),
                ("Deletion or return", "At the end of the services the processor must delete or return all personal data, at the controller's choice.", AuditSeverity.Medium),
                ("Audits and information", "The processor must make available the information needed to demonstrate compliance and allow audits or inspections.", AuditSeverity.Medium),
                ("International transfers", "Transfers of personal data outside the EEA are only made with appropriate safeguards, such as Standard Contractual Clauses.", AuditSeverity.High),
            ]),
    ];

    public static ChecklistTemplate? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
