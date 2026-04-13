export interface TrnItemDto {
  trnID: number;
  msgStr: string;
  retryCnt: number;
}

export interface PollResponseDto {
  hasData: boolean;
  needAckFirst?: boolean;
  batchToken?: string;
  totalPending: number;
  rows?: TrnItemDto[];
}

export interface AckRequestDto {
  batchToken: string;
  trnIDs: number[];
}