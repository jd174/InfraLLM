import { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement> & { size?: number };

const icon = (path: string) =>
  function Icon({ size = 18, className, ...props }: IconProps) {
    return (
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={1.75}
        strokeLinecap="round"
        strokeLinejoin="round"
        className={className}
        aria-hidden="true"
        {...props}
      >
        {path.split("|").map((d, i) => (
          <path key={i} d={d} />
        ))}
      </svg>
    );
  };

// Nav icons
export const ChatIcon = icon("M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z");
export const JobRunsIcon = icon("M9 12l2 2 4-4|M21 12c0 4.97-4.03 9-9 9s-9-4.03-9-9 4.03-9 9-9c2.4 0 4.58.94 6.19 2.47");
export const JobsIcon = icon("M12 2v4|M12 18v4|M4.93 4.93l2.83 2.83|M16.24 16.24l2.83 2.83|M2 12h4|M18 12h4|M4.93 19.07l2.83-2.83|M16.24 7.76l2.83-2.83");
export const HostsIcon = icon("M20 17H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2z|M8 21h8|M12 17v4");
export const McpIcon = icon("M12 2L2 7l10 5 10-5-10-5z|M2 17l10 5 10-5|M2 12l10 5 10-5");
export const CredentialsIcon = icon("M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4");
export const PoliciesIcon = icon("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z");
export const AuditIcon = icon("M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z|M14 2v6h6|M16 13H8|M16 17H8|M10 9H8");

// Action icons
export const PlusIcon = icon("M12 5v14|M5 12h14");
export const XIcon = icon("M18 6 6 18|M6 6l12 12");
export const ChevronRightIcon = icon("m9 18 6-6-6-6");
export const ChevronLeftIcon = icon("m15 18-6-6 6-6");
export const ChevronDownIcon = icon("m6 9 6 6 6-6");
export const MenuIcon = icon("M4 6h16|M4 12h16|M4 18h16");
export const LogoutIcon = icon("M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4|M16 17l5-5-5-5|M21 12H9");
export const RefreshIcon = icon("M21 12a9 9 0 1 1-9-9c2.52 0 4.85.99 6.57 2.57L21 8|M21 3v5h-5");
export const TrashIcon = icon("M3 6h18|M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2");
export const EditIcon = icon("M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7|M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z");
export const SendIcon = icon("M22 2 11 13|M22 2 15 22l-4-9-9-4 20-7z");
export const NotesIcon = icon("M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z|M14 2v6h6|M16 13H8|M16 17H8|M10 9H8");
export const SettingsIcon = icon("M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z|M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z");
export const CheckIcon = icon("M20 6 9 17l-5-5");
export const ServerIcon = icon("M2 8a4 4 0 0 1 4-4h12a4 4 0 0 1 4 4v8a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8z|M6 12h.01|M10 12h.01");
export const CopyIcon = icon("M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z|M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 0 2 2v1");
export const TokenIcon = icon("M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 0 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 0 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z|M16.5 7.5a1 1 0 1 0 0-2 1 1 0 0 0 0 2z");
