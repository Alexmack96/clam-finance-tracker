import { z } from "zod";

/** The widths of the `name` and `email` columns. 320 is also the RFC 5321 maximum. */
export const MAX_USER_NAME_LENGTH = 200;
export const MAX_USER_EMAIL_LENGTH = 320;

/**
 * Not a column width — the password is hashed before it is stored, so the column
 * never sees it. It is a bound on work: scrypt is deliberately expensive, and
 * hashing a megabyte of it is CPU spent on one unauthenticated request. 128 is
 * past any passphrase a person types and any password a manager generates.
 */
export const MAX_USER_PASSWORD_LENGTH = 128;

export const createUserSchema = z.object({
  name: z
    .string()
    .min(3, "Name must be at least 3 characters")
    .max(MAX_USER_NAME_LENGTH, "Name is too long"),
  email: z.string().email("Invalid email address").max(MAX_USER_EMAIL_LENGTH, "Email is too long"),
  password: z
    .string()
    .min(8, "Password must be at least 8 characters")
    .max(MAX_USER_PASSWORD_LENGTH, `Password must be ${MAX_USER_PASSWORD_LENGTH} characters or fewer`),
});

export type CreateUserInput = z.infer<typeof createUserSchema>;
