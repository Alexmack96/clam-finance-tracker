import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { signIn, useSession } from "../lib/authClient.js";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

const schema = z.object({
  email: z.email("Please enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type LoginFormValues = z.infer<typeof schema>;

export function LoginPage() {
  const navigate = useNavigate();
  const { data: session } = useSession();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({ resolver: zodResolver(schema) });

  if (session) {
    navigate("/dashboard", { replace: true });
    return null;
  }

  async function onSubmit(values: LoginFormValues) {
    const { error } = await signIn.email(values);
    if (error) {
      setError("root.serverError", { message: "Invalid email or password" });
    } else {
      navigate("/dashboard");
    }
  }

  return (
    <div className="min-h-screen relative flex items-stretch">
      <div className="app-atmosphere" aria-hidden />

      {/* Left editorial panel */}
      <aside className="hidden lg:flex flex-col justify-between w-[46%] xl:w-[40%] p-12 xl:p-16 relative">
        <div className="rise rise-1 flex items-center gap-2.5">
          <svg
            width="22"
            height="22"
            viewBox="0 0 28 28"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            className="text-primary"
          >
            <path
              d="M14 3 C7 3 3 8 3 14 L25 14 C25 8 21 3 14 3 Z"
              fill="currentColor"
              opacity="0.45"
            />
            <path d="M14 3 L14 14" stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M9.5 4.5 L11.5 14" stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M18.5 4.5 L16.5 14" stroke="currentColor" strokeWidth="1" opacity="0.65" />
            <path d="M5.5 8.5 L9 14" stroke="currentColor" strokeWidth="1" opacity="0.45" />
            <path d="M22.5 8.5 L19 14" stroke="currentColor" strokeWidth="1" opacity="0.45" />
            <path
              d="M3 14 C3 21 7.5 25 14 25 C20.5 25 25 21 25 14 Z"
              fill="currentColor"
              opacity="0.85"
            />
            <circle cx="14" cy="17" r="3.5" fill="white" opacity="0.92" />
            <circle cx="13" cy="16" r="1.2" fill="white" opacity="0.5" />
          </svg>
          <span className="font-display text-[19px] font-medium tracking-tight text-foreground">
            Clam<span className="font-light text-muted-foreground/70"> Finance</span>
          </span>
        </div>

        <div className="space-y-8">
          <p className="rise rise-2 eyebrow">Personal Finance Tracker</p>
          <h1 className="rise rise-3 font-display text-[clamp(2.6rem,5.4vw,4.8rem)] leading-[0.98] font-light text-foreground">
            Money, <span className="italic text-primary">tracked together</span>.
          </h1>
          <p className="rise rise-4 max-w-md text-[15px] leading-relaxed text-muted-foreground">
            Bank transactions in one place, joint expenses settled fairly, and the portfolio in
            plain sight.
          </p>
          <div className="rise rise-5 divider-rule max-w-md" />
          <dl className="rise rise-5 grid grid-cols-3 gap-6 max-w-md text-sm">
            <div>
              <dt className="eyebrow">Banks</dt>
              <dd className="font-display text-2xl font-light mt-1.5">07</dd>
            </div>
            <div>
              <dt className="eyebrow">Currency</dt>
              <dd className="font-display text-2xl font-light mt-1.5">GBP</dd>
            </div>
            <div>
              <dt className="eyebrow">Us</dt>
              <dd className="font-display text-2xl font-light mt-1.5">02</dd>
            </div>
          </dl>
        </div>

        <p className="rise rise-6 text-xs text-muted-foreground/60 font-numeric">
          &copy; {new Date().getFullYear()} &middot; built for the two of us
        </p>
      </aside>

      {/* Form panel */}
      <main className="flex-1 flex items-center justify-center p-8">
        <div className="w-full max-w-sm rise rise-3">
          <div className="lg:hidden flex items-center gap-2.5 mb-10 justify-center">
            <span className="font-display text-2xl font-medium text-foreground">
              Clam<span className="font-light text-muted-foreground/70"> Finance</span>
            </span>
          </div>

          <p className="eyebrow mb-3">Sign in</p>
          <h2 className="font-display text-4xl font-light text-foreground leading-tight">
            Welcome <span className="italic">back</span>.
          </h2>
          <p className="text-sm text-muted-foreground mt-2 mb-8">
            Sign in to continue your ledger.
          </p>

          <form onSubmit={handleSubmit(onSubmit)} className="space-y-5" noValidate>
            <div className="space-y-1.5">
              <Label htmlFor="email" className="eyebrow">
                Email
              </Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                placeholder="you@example.com"
                {...register("email")}
                className={`h-11 bg-card/60 backdrop-blur-sm ${errors.email ? "border-destructive focus-visible:ring-destructive" : ""}`}
              />
              {errors.email && (
                <p className="text-xs text-destructive mt-1">{errors.email.message}</p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="password" className="eyebrow">
                Password
              </Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                placeholder="••••••••••"
                {...register("password")}
                className={`h-11 bg-card/60 backdrop-blur-sm ${errors.password ? "border-destructive focus-visible:ring-destructive" : ""}`}
              />
              {errors.password && (
                <p className="text-xs text-destructive mt-1">{errors.password.message}</p>
              )}
            </div>
            {errors.root?.serverError && (
              <p className="text-xs text-destructive">{errors.root.serverError.message}</p>
            )}
            <Button
              type="submit"
              disabled={isSubmitting}
              className="w-full h-11 mt-2 font-medium tracking-tight"
            >
              {isSubmitting ? "Signing in..." : "Sign in"}
            </Button>
          </form>

          <p className="text-xs text-muted-foreground/70 mt-8 leading-relaxed">
            Locked out? Ask the admin (Alex) to reset your password.
          </p>
        </div>
      </main>
    </div>
  );
}
