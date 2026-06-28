import { Router } from "express";
import { randomUUID } from "crypto";
import { db } from "../db/client.js";
import { Prisma } from "../generated/prisma/index.js";
import { hashPassword } from "better-auth/crypto";
import { createUserSchema } from "@clam/core";

export const usersRouter = Router();

usersRouter.post("/", async (req, res) => {
  const parsed = createUserSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }

  const { name, email, password } = parsed.data;

  const existing = await db.user.findUnique({ where: { email } });
  if (existing) {
    res.status(409).json({ error: "Email already in use" });
    return;
  }

  const hash = await hashPassword(password);

  try {
    const user = await db.$transaction(async (tx) => {
      const created = await tx.user.create({
        data: { name, email },
        select: { id: true, name: true, email: true, createdAt: true },
      });
      await tx.account.create({
        data: {
          id: randomUUID(),
          accountId: created.id,
          providerId: "credential",
          userId: created.id,
          password: hash,
        },
      });
      return created;
    });

    res.status(201).json(user);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === "P2002") {
      res.status(409).json({ error: "Email already in use" });
      return;
    }
    throw err;
  }
});

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
