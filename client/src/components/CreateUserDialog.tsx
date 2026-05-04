import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createUserSchema, type CreateUserInput } from "@clam/core";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "./ui/dialog.js";
import { Input } from "./ui/input.js";
import { Label } from "./ui/label.js";
import { Button } from "./ui/button.js";
import api from "../lib/api.js";
import type { AxiosError } from "axios";

type User = {
  id: string;
  name: string;
  email: string;
  role: "Admin" | "User";
  createdAt: string;
};

export function CreateUserDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
}) {
  const queryClient = useQueryClient();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateUserInput>({
    resolver: zodResolver(createUserSchema),
  });

  const createUser = useMutation({
    mutationFn: async (data: CreateUserInput) => {
      const r = await api.post<User>("/api/admin/users", data);
      return r.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
      reset();
      createUser.reset();
      onOpenChange(false);
    },
    onError: (err: AxiosError<{ error?: string }>) => {
      setServerError(err.response?.data?.error ?? "Something went wrong");
    },
  });

  function handleOpenChange(v: boolean) {
    if (!v) {
      reset();
      setServerError(null);
      createUser.reset();
    }
    onOpenChange(v);
  }

  function onSubmit(data: CreateUserInput) {
    setServerError(null);
    createUser.mutate(data);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Create User</DialogTitle>
          <DialogDescription>Add a new user account. Role defaults to User.</DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} noValidate autoComplete="off">
          <div className="space-y-4 py-2">
            <div className="space-y-1">
              <Label htmlFor="cu-name">Name</Label>
              <Input
                id="cu-name"
                {...register("name")}
                placeholder="Jane Smith"
                autoComplete="off"
              />
              {errors.name && (
                <p className="text-sm text-destructive">{errors.name.message}</p>
              )}
            </div>
            <div className="space-y-1">
              <Label htmlFor="cu-email">Email</Label>
              <Input
                id="cu-email"
                type="email"
                {...register("email")}
                placeholder="jane@example.com"
              />
              {errors.email && (
                <p className="text-sm text-destructive">{errors.email.message}</p>
              )}
            </div>
            <div className="space-y-1">
              <Label htmlFor="cu-password">Password</Label>
              <Input
                id="cu-password"
                type="password"
                {...register("password")}
                placeholder="Min 8 characters"
                autoComplete="new-password"
              />
              {errors.password && (
                <p className="text-sm text-destructive">{errors.password.message}</p>
              )}
            </div>
            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          </div>
          <DialogFooter className="mt-4">
            <Button type="submit" disabled={createUser.isPending}>
              {createUser.isPending ? "Creating..." : "Create User"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
