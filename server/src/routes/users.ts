import { Router } from "express";
import { db } from "../db/client.js";

export const usersRouter = Router();

usersRouter.get("/", async (_req, res) => {
  const users = await db.user.findMany({
    select: {
      id: true,
      name: true,
      email: true,
      createdAt: true,
    },
    orderBy: { createdAt: "desc" },
  });
  res.json(users);
});
