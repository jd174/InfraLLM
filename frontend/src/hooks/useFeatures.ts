"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";

interface Features {
  chatEnabled: boolean;
  llmProvider?: string;
}

const DEFAULT: Features = { chatEnabled: true, llmProvider: "anthropic" };

// Module-level cache so layout + any child page share a single fetch.
let promise: Promise<Features> | null = null;
let resolved: Features | null = null;

export function useFeatures(): Features {
  const [features, setFeatures] = useState<Features>(resolved ?? DEFAULT);

  useEffect(() => {
    if (resolved) return;
    if (!promise) {
      promise = api
        .get<Features>("/api/config")
        .catch(() => DEFAULT); // fail open — don't break the app
    }
    promise.then((f) => {
      resolved = f;
      setFeatures(f);
    });
  }, []);

  return features;
}
