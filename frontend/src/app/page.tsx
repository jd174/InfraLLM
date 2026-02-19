import Link from "next/link";

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center p-8">
      <h1 className="text-4xl font-bold mb-4">InfraLLM</h1>
      <p className="text-lg text-gray-400 mb-8">
        AI-powered infrastructure management
      </p>
      <div className="flex gap-4">
        <Link
          href="/login"
          className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
        >
          Sign In
        </Link>
        <Link
          href="/register"
          className="px-6 py-3 border border-gray-600 text-gray-300 rounded-lg hover:bg-gray-800 transition"
        >
          Register
        </Link>
      </div>
    </main>
  );
}
