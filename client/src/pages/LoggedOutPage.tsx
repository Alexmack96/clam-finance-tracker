import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

export function LoggedOutPage() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <Card className="w-full max-w-sm text-center">
        <CardHeader className="items-center">
          <img
            src="/clam-app-logo.png"
            alt=""
            aria-hidden="true"
            className="size-16 rounded-full mb-2"
          />
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
