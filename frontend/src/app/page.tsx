import Link from "next/link";

export default function Home() {
  return (
    <main className="flex min-h-dvh flex-col items-center justify-center p-8 text-center">
      <h1 className="text-4xl font-bold mb-3 text-foreground">InfraLLM</h1>
      <p className="text-lg text-muted-foreground mb-2 max-w-xl">
        The secure MCP gateway to your infrastructure.
      </p>
      <p className="text-sm text-muted-foreground mb-8 max-w-xl">
        Bring your own AI — connect Claude, Cursor, or any MCP client and manage your
        servers with policy enforcement, encrypted credentials, and full audit logging.
      </p>
      <div className="flex gap-4">
        <Link
          href="/login"
          className="px-6 py-3 bg-primary text-primary-foreground rounded-lg hover:opacity-90 transition"
        >
          Sign In
        </Link>
        <Link
          href="/register"
          className="px-6 py-3 border border-border text-foreground rounded-lg hover:bg-muted transition"
        >
          Register
        </Link>
      </div>
    </main>
  );
}
