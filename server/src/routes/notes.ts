import { Router } from "express";
import { db } from "../db/client.js";
import { createNoteSchema, updateNoteSchema } from "@clam/core";

export const notesRouter = Router();

// GET /api/notes
notesRouter.get("/", async (_req, res) => {
  const notes = await db.note.findMany({
    orderBy: [{ pinned: "desc" }, { updatedAt: "desc" }],
  });
  res.json(notes);
});

// POST /api/notes
notesRouter.post("/", async (req, res) => {
  const body = createNoteSchema.parse(req.body);
  const note = await db.note.create({ data: body });
  res.status(201).json(note);
});

// PATCH /api/notes/:id
notesRouter.patch("/:id", async (req, res) => {
  const body = updateNoteSchema.parse(req.body);
  const note = await db.note.update({ where: { id: req.params.id }, data: body });
  res.json(note);
});

// DELETE /api/notes/:id
notesRouter.delete("/:id", async (req, res) => {
  await db.note.delete({ where: { id: req.params.id } });
  res.status(204).end();
});
