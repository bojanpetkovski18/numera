import { OrderStatusValue } from './grid-state';

export interface OrderRow {
  readonly id: number;
  readonly orderId: string;
  readonly orderDate: string;
  readonly customerId: string;
  readonly region: string;
  readonly channel: string;
  readonly productSku: string;
  readonly productCategory: string;
  readonly quantity: number;
  readonly unitPrice: number;
  readonly discount: number;
  readonly lineTotal: number;
  readonly status: OrderStatusValue;
}

export interface OrdersPage {
  readonly items: readonly OrderRow[];
  readonly totalCount: number;
  readonly page: number;
  readonly pageSize: number;
  readonly totalPages: number;
}