import { CloseIcon } from "./Icons";

interface Props {
  message: string;
  onDismiss: () => void;
}

export function ErrorBanner({ message, onDismiss }: Props) {
  return (
    <div className="error-banner" role="alert">
      <span className="state-dot state-error state-dot--pulse" />
      <span className="error-banner__text">{message}</span>
      <button className="error-banner__dismiss" onClick={onDismiss} aria-label="Dismiss error">
        <CloseIcon />
      </button>
    </div>
  );
}
