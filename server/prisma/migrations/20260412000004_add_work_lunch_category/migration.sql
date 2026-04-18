INSERT INTO "Category" ("id", "name", "color", "isFixed", "isDirectDebit")
VALUES (lower(hex(randomblob(9))), 'Work Lunch', '#f97316', 0, 0)
ON CONFLICT ("name") DO NOTHING;
