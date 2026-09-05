import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import RuleIcon from '@mui/icons-material/Rule';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { Alert, AlertTitle, Box, Chip, Divider, Paper, Stack, Tooltip, Typography } from '@mui/material';
import { observer } from 'mobx-react-lite';
import type { ProcessingSource } from '../api/types';
import type { SearchStore } from '../stores/SearchStore';

interface Props {
  store: SearchStore;
}

const SourceChip = ({ stage, source }: { stage: string; source: ProcessingSource | null }) => {
  if (source === null) {
    return null;
  }

  const isLlm = source === 'Llm';

  return (
    <Tooltip
      title={
        isLlm
          ? `${stage} was performed by the language model.`
          : `${stage} used the built-in deterministic rules instead of the language model.`
      }
    >
      <Chip
        size="small"
        icon={isLlm ? <AutoAwesomeIcon /> : <RuleIcon />}
        label={`${stage}: ${isLlm ? 'AI' : 'Built-in rules'}`}
        color={isLlm ? 'primary' : 'default'}
        variant={isLlm ? 'filled' : 'outlined'}
      />
    </Tooltip>
  );
};

/**
 * Shows how the query was read and which engine did the work.
 *
 * The brief's requirement is that a weaker answer never looks like a strong one, so the
 * fallback disclaimer is a full alert rather than a subtle badge, and it repeats the reason
 * the API gave rather than inventing its own wording.
 */
export const InterpretationPanel = observer(({ store }: Props) => {
  const response = store.response;

  if (!response) {
    return null;
  }

  const { interpretation } = response;
  const hasInterpretation =
    interpretation.title || interpretation.author || interpretation.keywords.length > 0;

  return (
    <Stack spacing={2}>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          sx={{
            justifyContent: 'space-between',
            alignItems: { xs: 'flex-start', md: 'center' },
          }}
        >
          <Box>
            <Typography variant="overline" color="text.secondary">
              Understood your search as
            </Typography>

            {hasInterpretation ? (
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 0.5 }}>
                {interpretation.title ? (
                  <Chip size="small" label={`Title: ${interpretation.title}`} />
                ) : null}
                {interpretation.author ? (
                  <Chip size="small" label={`Author: ${interpretation.author}`} />
                ) : null}
                {interpretation.keywords.map((keyword) => (
                  <Chip key={keyword} size="small" variant="outlined" label={keyword} />
                ))}
              </Stack>
            ) : (
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                No title or author could be identified, so the whole phrase was searched.
              </Typography>
            )}
          </Box>

          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <SourceChip stage="Interpretation" source={store.extractionSource} />
            <SourceChip stage="Ranking" source={store.rankingSource} />
          </Stack>
        </Stack>
      </Paper>

      {store.usedFallback ? (
        <Alert severity="info" icon={<WarningAmberIcon />} variant="outlined">
          <AlertTitle sx={{ fontWeight: 600 }}>
            These results did not use AI, and may be less accurate
          </AlertTitle>

          <Typography variant="body2">
            Part of this search ran on built-in rules rather than the language model. Ordering
            and reasoning are still based on real catalogue data, but the rules are simpler:
            they can miss a misspelled author, or read a title as keywords.
          </Typography>

          {store.fallbackReasons.length > 0 ? (
            <>
              <Divider sx={{ my: 1.25 }} />
              <Stack component="ul" spacing={0.5} sx={{ m: 0, pl: 2.5 }}>
                {store.fallbackReasons.map((reason) => (
                  <Typography key={reason} component="li" variant="body2" color="text.secondary">
                    {reason}
                  </Typography>
                ))}
              </Stack>
            </>
          ) : null}
        </Alert>
      ) : null}
    </Stack>
  );
});
