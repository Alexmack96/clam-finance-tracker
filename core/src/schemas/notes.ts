import { z } from "zod";

/**
 * The width of the `title` column. Without it here the client happily submits a
 * title the server then refuses, and the user sees a failed request instead of a
 * field error. `body` needs no equivalent — that column is unbounded text.
 */
export const MAX_NOTE_TITLE_LENGTH = 300;

export const createNoteSchema = z.object({
  title: z.string().min(1, "Title is required").max(MAX_NOTE_TITLE_LENGTH, "Title is too long"),
  body: z.string().optional().nullable(),
  pinned: z.boolean().optional(),
});

export const updateNoteSchema = z.object({
  title: z.string().min(1).max(MAX_NOTE_TITLE_LENGTH, "Title is too long").optional(),
  body: z.string().optional().nullable(),
  pinned: z.boolean().optional(),
});

export type CreateNoteInput = z.infer<typeof createNoteSchema>;
export type UpdateNoteInput = z.infer<typeof updateNoteSchema>;

export type Note = {
  id: string;
  title: string;
  body: string | null;
  pinned: boolean;
  createdAt: string;
  updatedAt: string;
};
