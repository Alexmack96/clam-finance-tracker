import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export function LoggedOutPage() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <Card className="w-full max-w-sm text-center">
        <CardHeader className="items-center">
          <svg
            width="48"
            height="48"
            viewBox="0 0 28 28"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            aria-hidden="true"
            className="text-primary mb-2"
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
          <CardTitle className="text-2xl">Signed out</CardTitle>
          <CardDescription>
            You have been successfully signed out of Clam Finance Tracker.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button className="w-full" onClick={() => navigate("/login")}>
            Sign back in
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
