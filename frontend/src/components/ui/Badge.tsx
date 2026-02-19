import { cn } from "@/lib/utils";
import { HTMLAttributes } from "react";

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "success" | "warning" | "danger" | "neutral";
}

export function Badge({ className, variant = "default", ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        {
          "bg-primary/10 text-primary": variant === "default",
          "bg-green-500/10 text-green-400": variant === "success",
          "bg-yellow-500/10 text-yellow-400": variant === "warning",
          "bg-red-500/10 text-red-400": variant === "danger",
          "bg-muted text-muted-foreground": variant === "neutral",
        },
        className
      )}
      {...props}
    />
  );
}
