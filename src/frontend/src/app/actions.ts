"use server";

import { refresh } from "next/cache";

import { ApiError, deleteDocument, reprocessDocument, uploadDocument } from "@/lib/api";
import { MAX_FILE_SIZE_BYTES } from "@/lib/documents";

export type UploadState = { status: "idle" | "success" | "error"; message?: string };

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  return "Could not reach the DocuMind API. Is the backend running?";
}

export async function uploadDocumentAction(_prev: UploadState, formData: FormData): Promise<UploadState> {
  // Server Actions are public endpoints, so validate again here rather than trusting the client
  const file = formData.get("file");
  const title = String(formData.get("title") ?? "").trim();

  if (!(file instanceof File) || file.size === 0) {
    return { status: "error", message: "Choose a PDF file to upload." };
  }
  if (file.size > MAX_FILE_SIZE_BYTES) {
    return { status: "error", message: "Files larger than 50 MB are not allowed." };
  }

  try {
    const document = await uploadDocument(file, title);
    refresh();
    return { status: "success", message: `Uploaded "${document.title}".` };
  } catch (error) {
    return { status: "error", message: errorMessage(error) };
  }
}

export async function reprocessDocumentAction(id: string): Promise<{ error?: string }> {
  if (!GUID_PATTERN.test(id)) {
    return { error: "Invalid document id." };
  }

  try {
    await reprocessDocument(id);
    refresh();
    return {};
  } catch (error) {
    return { error: errorMessage(error) };
  }
}

export async function deleteDocumentAction(id: string): Promise<{ error?: string }> {
  if (!GUID_PATTERN.test(id)) {
    return { error: "Invalid document id." };
  }

  try {
    await deleteDocument(id);
    refresh();
    return {};
  } catch (error) {
    return { error: errorMessage(error) };
  }
}
