// Shared between server and client code: types and limits only, no API access

export type DocumentStatus = "Pending" | "Processing" | "Processed" | "Failed";

export type DocumentSummary = {
  id: string;
  title: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
  uploadedAt: string;
  status: DocumentStatus;
};

// Must match DocumentsController.MaxFileSizeBytes in the API
export const MAX_FILE_SIZE_BYTES = 50 * 1024 * 1024;

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
