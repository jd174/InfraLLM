import { cn } from "@/lib/utils";
import { HTMLAttributes } from "react";

interface AlertProps extends HTMLAttributes<HTMLDivElement> {
  variant?: "error" | "success" | "warning" | "info";
}

export function Alert({ className, variant = "error", ...props }: AlertProps) {
  return (
    <div
      className={cn(
        "rounded-lg border px-4 py-3 text-sm",
        {
          "bg-red-500/10 border-red-500/20 text-red-400": variant === "error",
          "bg-green-500/10 border-green-500/20 text-green-400": variant === "success",
          "bg-yellow-500/10 border-yellow-500/20 text-yellow-400": variant === "warning",
          "bg-primary/10 border-primary/20 text-primary": variant === "info",
        },
        className
      )}
      {...props}
    />
  );
}
