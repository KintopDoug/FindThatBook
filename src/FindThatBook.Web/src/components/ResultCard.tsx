import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import MenuBookIcon from '@mui/icons-material/MenuBook';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import RuleIcon from '@mui/icons-material/Rule';
import { Avatar, Box, Card, CardContent, Link, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { BookCandidate, ProcessingSource } from '../api/types';

interface Props {
  candidate: BookCandidate;
  rank: number;
  rankingSource: ProcessingSource | null;
}

/** Cover art, falling back to a glyph when Open Library has no image or serves a broken one. */
const Cover = ({ candidate }: { candidate: BookCandidate }) => {
  const [failed, setFailed] = useState(false);
  const size = { width: 72, height: 104 };

  // Open Library returns broken or placeholder images for some works, so a failed load has
  // to land on the same glyph as a missing one. Hiding the image instead would leave a hole
  // beside a real result.
  if (!candidate.coverImageUrl || failed) {
    return (
      <Box
        sx={{
          ...size,
          flexShrink: 0,
          borderRadius: 1,
          bgcolor: 'action.hover',
          display: 'grid',
          placeItems: 'center',
          color: 'text.disabled',
        }}
      >
        <MenuBookIcon />
      </Box>
    );
  }

  return (
    <Box
      component="img"
      src={candidate.coverImageUrl}
      alt=""
      loading="lazy"
      onError={() => setFailed(true)}
      sx={{ ...size, flexShrink: 0, borderRadius: 1, objectFit: 'cover', bgcolor: 'action.hover' }}
    />
  );
};

export const ResultCard = ({ candidate, rank, rankingSource }: Props) => {
  const authors = candidate.authors.length > 0 ? candidate.authors.join(', ') : 'Author unknown';
  const rankedByAi = rankingSource === 'Llm';

  return (
    <Card variant="outlined">
      <CardContent>
        <Stack direction="row" spacing={2}>
          {/* Position is the primary signal of match strength; the brief asks for ordering
              rather than a numeric score, so the rank is shown and no score is. */}
          <Avatar
            sx={{
              width: 30,
              height: 30,
              fontSize: '0.85rem',
              fontWeight: 700,
              bgcolor: rank === 1 ? 'primary.main' : 'action.selected',
              color: rank === 1 ? 'primary.contrastText' : 'text.secondary',
            }}
          >
            {rank}
          </Avatar>

          <Cover candidate={candidate} />

          <Box sx={{ minWidth: 0, flexGrow: 1 }}>
            <Typography variant="h2" sx={{ wordBreak: 'break-word' }}>
              {candidate.title}
            </Typography>

            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>
              {authors}
              {candidate.firstPublishYear ? ` · first published ${candidate.firstPublishYear}` : ''}
            </Typography>

            {/* The grounded explanation, given real weight rather than tucked away: it is
                what tells the reader why this book is here at all. */}
            <Stack
              direction="row"
              spacing={1}
              sx={{
                mt: 1.5,
                p: 1.25,
                borderRadius: 1,
                bgcolor: 'action.hover',
                alignItems: 'flex-start',
              }}
            >
              <Box sx={{ color: 'text.secondary', display: 'flex', pt: '2px' }}>
                {rankedByAi ? (
                  <AutoAwesomeIcon fontSize="small" />
                ) : (
                  <RuleIcon fontSize="small" />
                )}
              </Box>
              <Box>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                  {rankedByAi ? 'Why the AI ranked this here' : 'Why the rules ranked this here'}
                </Typography>
                <Typography variant="body2">{candidate.explanation}</Typography>
              </Box>
            </Stack>

            {candidate.openLibraryUrl ? (
              <Link
                href={candidate.openLibraryUrl}
                target="_blank"
                rel="noopener noreferrer"
                variant="body2"
                sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5, mt: 1.5 }}
              >
                View on Open Library
                <OpenInNewIcon sx={{ fontSize: 14 }} />
              </Link>
            ) : null}
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
};
