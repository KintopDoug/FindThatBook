import RefreshIcon from '@mui/icons-material/Refresh';
import { Alert, AlertTitle, Box, Button, Stack, Typography } from '@mui/material';
import type { ApiError } from '../api/ApiError';

interface Props {
  error: ApiError;
  onRetry?: () => void;
}

/**
 * Renders a failure from the API's problem document.
 *
 * The API answers every failure the same way, so there is one component for all of them.
 * What changes is the framing: a 4xx is something the reader can fix by editing the query,
 * a 5xx is something they can only wait out. The traceId is shown small but shown, because
 * it is the one thing that lets someone find this exact failure in the server logs.
 */
export const ErrorNotice = ({ error, onRetry }: Props) => {
  const severity = error.isUserFixable ? 'warning' : 'error';

  return (
    <Alert
      severity={severity}
      variant="outlined"
      sx={{ alignItems: 'flex-start' }}
      action={
        error.isRetryable && onRetry ? (
          <Button color="inherit" size="small" startIcon={<RefreshIcon />} onClick={onRetry}>
            Try again
          </Button>
        ) : null
      }
    >
      <AlertTitle sx={{ fontWeight: 600 }}>{error.title}</AlertTitle>

      <Typography variant="body2">{error.detail}</Typography>

      {error.isUserFixable ? (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          Adjust your search and try again.
        </Typography>
      ) : null}

      {error.traceId ? (
        <Box sx={{ mt: 1.5 }}>
          <Stack direction="row" spacing={0.75} sx={{ alignItems: 'baseline', flexWrap: 'wrap' }}>
            <Typography variant="caption" color="text.secondary">
              Reference
            </Typography>
            <Typography
              variant="caption"
              sx={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', wordBreak: 'break-all' }}
              color="text.secondary"
            >
              {error.traceId}
            </Typography>
          </Stack>
        </Box>
      ) : null}
    </Alert>
  );
};
