import { ImportPreview } from './import.models';

/** The second sample file's columns (web/public/sample-orders-mapped.csv), as the preview suggests them. */
const HEADERS = ['Order Number', 'Date', 'Customer', 'Sales Region', 'Sales Channel', 'SKU', 'Category', 'Qty', 'Price', 'Discount %', 'Order Status', 'Notes'];

export const COLUMNS: Record<string, string | null> = {
  order_id: 'Order Number',
  order_date: 'Date',
  customer_id: 'Customer',
  region: 'Sales Region',
  channel: 'Sales Channel',
  product_sku: 'SKU',
  product_category: 'Category',
  quantity: 'Qty',
  unit_price: 'Price',
  discount: 'Discount %',
  status: 'Order Status',
};

export const MAPPED_PREVIEW: ImportPreview = {
  headers: HEADERS,
  rows: [['SAMPLE-0001', '01/03/2025', 'C-00001', 'Europe', 'Online Store', 'HP-SO-ETH', 'Single Origin', '2', '18.50', '5', 'completed', 'First order']],
  matchesNumeraFormat: false,
  suggestion: { columns: COLUMNS, dateFormat: 'dd/MM/yyyy', discount: 'percent' },
  dateFormatAmbiguous: true,
  unmapped: [],
  savedMapping: null,
};
