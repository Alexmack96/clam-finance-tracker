import { useState } from "react";
import { Navigate, useSearchParams } from "react-router-dom";
import { useAuth } from "@workos-inc/authkit-react";
import { Button } from "@/components/ui/button";

/// The one failure the SDK cannot report itself, spelled out.
///
/// A blocked token exchange is almost always the origin missing from the
/// allowed-origins list, which is a *separate* dashboard setting from the
/// redirect URI. Getting the redirect URI right and stopping there gets you here.
const EXCHANGE_FAILED =
  "Signed in with WorkOS, but this app could not complete the token exchange. " +
  "Add this exact origin to the allowed origins list on the WorkOS dashboard's " +
  "Authentication page — that list is separate from Redirects, and both need it.";

/// The email and password form is gone. WorkOS AuthKit hosts sign-in now, so
/// this page's only job is to send the browser there and to say where to come
/// back to.
///
/// The editorial panel stays, because it is the first thing anyone sees and the
/// redirect is fast enough that a bare spinner would look like a broken link.
export function LoginPage() {
  const { user, isLoading, signIn } = useAuth();
  const [failed, setFailed] = useState<string | null>(null);
  const [searchParams] = useSearchParams();

  // Set by ProtectedRoute when a sign-in came back and then failed, rather than
  // never having happened.
  const authError = searchParams.get("authError");
  const message = failed ?? (authError === "exchange" ? EXCHANGE_FAILED : authError);

  if (user) return <Navigate to="/dashboard" replace />;

  async function onSignIn() {
    setFailed(null);
    try {
      await signIn({ state: { returnTo: "/dashboard" } });
    } catch {
      setFailed("Couldn't reach WorkOS. Check your connection and try again.");
    }
  }

  return (
    <div className="min-h-screen relative flex items-stretch">
      <div className="app-atmosphere" aria-hidden />

      {/* Left editorial panel */}
      <aside className="hidden lg:flex flex-col justify-between w-[46%] xl:w-[40%] p-12 xl:p-16 relative">
        <div className="rise rise-1 flex items-center gap-2">
          <img src="/clam-app-logo.png" alt="" aria-hidden="true" className="size-8 rounded-full" />
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
          <div className="lg:hidden flex flex-col items-center gap-2 mb-10">
            <img src="/clam-app-logo.png" alt="Clam Finance" className="size-16 rounded-full" />
            <span className="font-display text-2xl font-medium text-foreground">
              Clam<span className="font-light text-muted-foreground/70"> Finance</span>
            </span>
          </div>

          <div className="hidden lg:flex flex-col items-center mb-8">
            <img src="/clam-app-logo.png" alt="Clam Finance" className="size-20 rounded-full" />
          </div>

          <p className="eyebrow mb-3">Sign in</p>
          <h2 className="font-display text-4xl font-light text-foreground leading-tight">
            Welcome <span className="italic">back</span>.
          </h2>
          <p className="text-sm text-muted-foreground mt-2 mb-8">
            Sign in to continue your ledger.
          </p>

          <Button
            onClick={onSignIn}
            disabled={isLoading}
            className="w-full h-11 mt-2 font-medium tracking-tight"
          >
            {isLoading ? "Checking your session..." : "Continue with WorkOS"}
          </Button>

          {message && (
            <p className="text-xs text-destructive mt-3 leading-relaxed">
              {message}
              {authError === "exchange" && (
                <>
                  {" "}
                  This origin is <span className="font-mono">{window.location.origin}</span>.
                </>
              )}
            </p>
          )}

          <p className="text-xs text-muted-foreground/70 mt-8 leading-relaxed">
            Access is by invitation. Ask Alex to send you one from the WorkOS dashboard.
          </p>
        </div>
      </main>
    </div>
  );
}
