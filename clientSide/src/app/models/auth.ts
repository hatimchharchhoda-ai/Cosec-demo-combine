export interface LoginRequestDto {
  DeviceID: number;
  MACAddr: string;
  IPAddr: string;
}

export interface LoginResponseDto {
  success: boolean;
  message: string;
  deviceName: string;
  deviceType: string;
}