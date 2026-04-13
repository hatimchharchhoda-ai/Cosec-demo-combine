export interface PollRequestDto {
  lastBatchToken?: string | null;
}

export interface TrnItemDto {
  trnID: number;
  msgStr: string;
  retryCnt: number;
}

export interface PollResponseDto {
  hasData: boolean;
  batchToken?: string;
  pendingAckRequired?: boolean;
  pendingBatchToken?: string;
  items?: TrnItemDto[];
}

export interface AckRequestDto {
  batchToken: string;
  ackedTrnIDs: number[];
}